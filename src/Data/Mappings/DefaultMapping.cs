﻿namespace Sitecore.Data.Mappings
{
  using System.Collections.Generic;
  using System.Linq;
  using System.Xml;

  using Sitecore.Data;
  using Sitecore.Data.Helpers;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;

  public class DefaultMapping : AbstractMapping
  {
    [UsedImplicitly]
    public DefaultMapping([NotNull] XmlElement mappingElement) : base(mappingElement)
    {
      Assert.ArgumentNotNull(mappingElement, "mappingElement");
    }

    [NotNull]
    protected override IEnumerable<JsonItem> Initialize([NotNull] string json)
    {
      Assert.ArgumentNotNull(json, "json");

        var dictionary = JsonHelper.Deserialize<IDictionary<string, List<JsonItem>>>(json);
        if (dictionary == null)
        {
          return new List<JsonItem>();
        }

        var children = dictionary.Where(x => x.Key as object != null && x.Value != null).Select(x => new JsonItem(ID.Parse(x.Key), ID.Null, new JsonChildren(x.Value))).ToList();

        foreach (var item in children)
        {
          item.ParentID = ID.Null;

          this.InitializeItemTree(item);
        }

        return children;
    }

    public override IEnumerable<ID> GetChildIDs(ID itemId)
    {
      Assert.ArgumentNotNull(itemId, "itemId");

      var item = this.GetItem(itemId);

      // no need to check: item.ParentID == ID.Null
      if (item != null)
      {
        return item.Children.Select(x => x.ID);
      }

      return null;
    }


    protected override bool IgnoreItem(JsonItem item)
    {
      return item.ParentID == ID.Null;
    }

    public override bool CreateItem(ID itemID, string itemName, ID templateID, ID parentID)
    {
      Assert.ArgumentNotNull(itemID, "itemID");
      Assert.ArgumentNotNull(itemName, "itemName");
      Assert.ArgumentNotNull(templateID, "templateID");
      Assert.ArgumentNotNull(parentID, "parentID");

      var parent = this.ItemsCache.FirstOrDefault(x => x.ID == parentID);

      // no need to check: parent.ParentID == ID.Null
      if (parent == null)
      {
        // return false;
        parent = this.AddRootItem(parentID);
      }

      var item = new JsonItem(itemID, parentID)
        {
          Name = itemName,
          TemplateID = templateID
        };

      lock (this.SyncRoot)
      {
        this.ItemsCache.Add(item);

        parent.Children.Add(item);

        this.Commit();
      }

      return true;
    }

    public override bool CopyItem(ID sourceItemID, ID destinationItemID, ID copyID, string copyName)
    {
      Assert.ArgumentNotNull(sourceItemID, "sourceItemID");
      Assert.ArgumentNotNull(destinationItemID, "destinationItemID");
      Assert.ArgumentNotNull(copyID, "copyID");
      Assert.ArgumentNotNull(copyName, "copyName");

      var sourceItem = this.GetItem(sourceItemID);
      if (sourceItem == null || this.IgnoreItem(sourceItem))
      {
        return false;
      }

      var destinationItem = this.GetItem(destinationItemID);

      // no need to check: destinationItem.ParentID == ID.Null
      if (destinationItem == null)
      {
        return false;
      }

      return this.DoCopyItem(destinationItemID, copyID, copyName, sourceItem);
    }

    public override bool MoveItem(ID itemID, ID targetID)
    {
      Assert.ArgumentNotNull(itemID, "itemID");
      Assert.ArgumentNotNull(targetID, "targetID");

      var item = this.GetItem(itemID);
      if (item == null || this.IgnoreItem(item))
      {
        return false;
      }

      var target = this.GetItem(targetID);
      if (target == null)
      {
        target = this.AddRootItem(targetID);
      }

      var parent = this.GetItem(item.ParentID);
      Assert.IsNotNull(parent, "Cannot find {0} item", item.ParentID);
      
      lock (this.SyncRoot)
      {
        parent.Children.Remove(item);
        target.Children.Add(item);

        this.Commit();
      }

      return true;
    }

    protected override void DoDeleteItem(JsonItem item)
    {
      Assert.ArgumentNotNull(item, "item");

      var parentID = item.ParentID;
      var parent = this.GetItem(parentID);
      Assert.IsNotNull(parent, "parent");

      parent.Children.Remove(item);
    }
    protected override object GetCommitObject()
    {
      return this.ItemChildren.ToDictionary(x => x.ID.ToString(), x => x.Children);
    }

    [NotNull]
    private JsonItem AddRootItem([NotNull] ID itemID)
    {
      Assert.ArgumentNotNull(itemID, "itemID");

      var rootItem = new JsonItem(itemID, ID.Null)
        {
          Name = "$default-mapping"
        };
      
      this.ItemChildren.Add(rootItem);
      this.ItemsCache.Add(rootItem);

      return rootItem;
    }
  }
}
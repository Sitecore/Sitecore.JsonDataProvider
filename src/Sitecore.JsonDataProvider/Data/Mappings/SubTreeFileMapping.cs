﻿namespace Sitecore.Data.Mappings
{
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Xml;

  using Sitecore.Data;
  using Sitecore.Data.DataProviders;
  using Sitecore.Data.Helpers;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;

  public class SubTreeFileMapping : AbstractFileMapping
  {
    [NotNull]
    public readonly ID ItemID;

    [UsedImplicitly]
    public SubTreeFileMapping([NotNull] XmlElement mappingElement, [NotNull] string databaseName)
      : base(mappingElement, databaseName)
    {
      Assert.ArgumentNotNull(mappingElement, nameof(mappingElement));
      Assert.ArgumentNotNull(databaseName, nameof(databaseName));

      var itemString = mappingElement.GetAttribute("item");
      Assert.IsNotNull(itemString, $"The \"item\" attribute is not specified or has empty string value: {mappingElement.OuterXml}");

      ID itemID;
      ID.TryParse(itemString, out itemID);
      Assert.IsNotNull(itemID, $"the \"item\" attribute is not a valid GUID value: {mappingElement.OuterXml}");

      this.ItemID = itemID;
    }

    protected override IEnumerable<JsonItem> Initialize(FileInfo file)
    {
      Assert.ArgumentNotNull(file, nameof(file));

      var children = Deserialize(file);
      if (children == null)
      {
        return new List<JsonItem>();
      }

      foreach (var item in children)
      {
        item.ParentID = this.ItemID;
        this.InitializeItemTree(item);
      }

      return children;
    }

    [CanBeNull]
    private List<JsonItem> Deserialize(FileInfo file)
    {
      Assert.ArgumentNotNull(file, nameof(file));

      var filePath = file.FullName;
      Assert.ArgumentCondition(file.Exists, nameof(file), $"The {filePath} file does not exist");

      try
      {
        using (var reader = file.OpenText())
        {
          return JsonHelper.Deserialize<List<JsonItem>>(reader);
        }
      }
      catch
      {
        using (var reader = file.OpenText())
        {
          var dict = JsonHelper.Deserialize<Dictionary<ID, List<JsonItem>>>(reader);
          if (dict == null)
          {
            return null;
          }

          if (dict.Count == 0)
          {
            return null;
          }

          Assert.IsTrue(dict.Count == 1, "The root dictionary of the {0} file contains more than single key", filePath);

          var actualId = dict.Keys.Single();
          var expectedId = this.ItemID;
          Assert.IsTrue(actualId == expectedId, "The value ({0}) of single key of the root dictionary of the {1} file differs from the one specified in the mapping configuration ({2})", actualId, filePath, expectedId);

          return dict.Values.Single();
        }
      }
    }

    public override IEnumerable<ID> GetChildIDs(ID itemId)
    {
      Assert.ArgumentNotNull(itemId, nameof(itemId));
      Lock.EnterReadLock();
      try
      {
        if (itemId == this.ItemID)
        {
          return this.ItemChildren.Select(x => x.ID);
        }

        var item = this.ItemsCache[itemId];
        return item?.Children.Select(x => x.ID);
      }
      finally
      {
        Lock.ExitReadLock();
      }
    }

    public override bool AcceptsNewChildrenOf(ID itemID)
    {
      Assert.ArgumentNotNull(itemID, nameof(itemID));

      if (ReadOnly)
      {
        return false;
      }

      if (itemID == this.ItemID)
      {
        return true;
      }

      Lock.EnterReadLock();
      try
      {
        return this.ItemsCache.ContainsKey(itemID);
      }
      finally
      {
        Lock.ExitReadLock();
      }

    }

    protected override bool IgnoreItem(JsonItem item)
    {
      return false;
    }

    public override bool CreateItem(ID itemID, string itemName, ID templateID, ID parentID)
    {
      Assert.ArgumentNotNull(itemID, nameof(itemID));
      Assert.ArgumentNotNull(itemName, nameof(itemName));
      Assert.ArgumentNotNull(templateID, nameof(templateID));
      Assert.ArgumentNotNull(parentID, nameof(parentID));

      if (this.ReadOnly)
      {
        return false;
      }

      JsonItem parent = null;

      if (this.ItemID != parentID)
      {
        parent = this.GetItem(parentID);
        if (parent == null || this.IgnoreItem(parent))
        {
          return false;
        }
      }

      var item = new JsonItem(itemID, parentID)
      {
        Name = itemName,
        TemplateID = templateID
      };

      Lock.EnterWriteLock();
      try
      {
        this.ItemsCache[item.ID] = item;

        if (parent != null)
        {
          parent.Children.Add(item);
        }
        else
        {
          this.ItemChildren.Add(item);
        }
      }
      finally
      {
        Lock.ExitWriteLock();
      }

      this.Commit();

      return true;
    }

    public override bool CopyItem(ID sourceItemID, ID destinationItemID, ID copyID, string copyName, CallContext context)
    {
      Assert.ArgumentNotNull(sourceItemID, nameof(sourceItemID));
      Assert.ArgumentNotNull(destinationItemID, nameof(destinationItemID));
      Assert.ArgumentNotNull(copyID, nameof(copyID));
      Assert.ArgumentNotNull(copyName, nameof(copyName));

      if (this.ReadOnly)
      {
        return false;
      }

      List<JsonItem> children;

      Lock.EnterReadLock();
      try
      {
        children = this.ItemChildren;
        if (destinationItemID != this.ItemID)
        {
          var destinationItem = this.ItemsCache[destinationItemID];
          if (destinationItem == null)
          {
            return false;
          }

          children = destinationItem.Children;
        }
      }
      finally
      {
        Lock.ExitReadLock();
      }

      Lock.EnterWriteLock();
      try
      {
        var item = DoCopy(sourceItemID, destinationItemID, copyID, copyName, context);

        this.ItemsCache[item.ID] = item;

        children.Add(item);
      }
      finally
      {
        Lock.ExitWriteLock();
      }

      this.Commit();

      return true;
    }

    public override bool MoveItem(ID itemID, ID targetID)
    {
      Assert.ArgumentNotNull(itemID, nameof(itemID));
      Assert.ArgumentNotNull(targetID, nameof(targetID));

      if (this.ReadOnly)
      {
        return false;
      }

      JsonItem item;
      ID parentID;

      Lock.EnterReadLock();
      try
      {
        item = this.ItemsCache[itemID];
        if (item == null)
        {
          return false;
        }

        parentID = item.ParentID;
        if (parentID == targetID)
        {
          return true;
        }
      }
      finally
      {
        Lock.ExitReadLock();
      }

      Lock.EnterWriteLock();
      try
      {
        if (parentID == this.ItemID)
        {
          var target = this.ItemsCache[targetID];
          Assert.IsNotNull(target, $"Moving item outside of ItemChildrenMapping ({this.ItemID}, {this.DisplayName}) is not supported");

          this.ItemChildren.Remove(item);
          target.Children.Add(item);
        }
        else if (targetID == this.ItemID)
        {
          var parent = this.ItemsCache[parentID];
          Assert.IsNotNull(parent, $"Cannot find {parentID} item");

          parent.Children.Remove(item);
          this.ItemChildren.Add(item);
        }
        else
        {
          var parent = this.ItemsCache[parentID];
          Assert.IsNotNull(parent, $"Cannot find {parentID} item");

          var target = this.ItemsCache[targetID];
          Assert.IsNotNull(target, $"Moving item outside of ItemChildrenMapping ({this.ItemID}, {this.DisplayName}) is not supported");

          parent.Children.Remove(item);
          target.Children.Add(item);
        }
      }
      finally
      {
        Lock.ExitWriteLock();
      }

      this.Commit();

      return true;
    }

    protected override void DoDeleteItem(JsonItem item)
    {
      Assert.ArgumentNotNull(item, nameof(item));

      // no need in lock
      if (item.ParentID == this.ItemID)
      {
        this.ItemChildren.Remove(item);
      }
      else
      {
        var parentID = item.ParentID;
        var parent = this.ItemsCache[parentID];
        Assert.IsNotNull(parent, "parent");

        parent.Children.Remove(item);
      }
    }

    protected override object GetCommitObject()
    {
      // no need in lock
      return this.ItemChildren;
    }
  }
}
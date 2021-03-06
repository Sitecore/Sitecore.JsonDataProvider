﻿namespace Sitecore.Data.Converters
{
  using System;
  using System.Collections.Generic;

  using Newtonsoft.Json;

  using Sitecore.Data.Collections;
  using Sitecore.Data.DataProviders;
  using Sitecore.Data.Helpers;
  using Sitecore.Diagnostics;

  public class JsonUnversionedFieldsCollectionConverter : JsonConverter
  {
    public override bool CanRead => false;

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => Throw.NotImplementedException();

    public override bool CanConvert(Type objectType) => false;

    public override void WriteJson([NotNull] JsonWriter writer, [NotNull] object value, [NotNull] JsonSerializer serializer)
    {
      Assert.ArgumentNotNull(writer, nameof(writer));
      Assert.ArgumentNotNull(value, nameof(value));
      Assert.ArgumentNotNull(serializer, nameof(serializer));

      var any = false;
      writer.WriteStartObject();
      var dictionary = (Dictionary<string, JsonFieldsCollection>)value;
      foreach (var pair in dictionary)
      {
        var fieldsCollection = pair.Value;
        if (fieldsCollection == null || fieldsCollection.Count == 0)
        {
          continue;
        }

        any = true;
        writer.WritePropertyName(pair.Key);
        serializer.Serialize(writer, pair.Value);
      }

      if (JsonDataProvider.Settings.BetterMerging)
      {
        if (any)
        {
          writer.WriteRaw(",");
        }
        else
        {
          JsonHelper.WriteLineBreak(writer);
        }
      }

      writer.WriteEndObject();
    }
  }
}
using System.Collections.Generic;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace CastleStoryPlus.Port;

// Copies an object's values into the same kind of object of another Unity version, matching fields by name: what the
// target added keeps its own value (array elements beyond the target's own take a copy of its first element), what the
// target dropped is left out.
public static class FieldCopy
{
	public static void Copy(AssetTypeValueField source, AssetTypeValueField target)
	{
		if (target.TemplateField.IsArray)
		{
			CopyArray(source, target);
			return;
		}
		// A field with a value of its own (ValueBuilder gives other fields an empty value too).
		if (target.TemplateField.ValueType != AssetValueType.None)
		{
			if (source.Value != null && source.TemplateField.ValueType != AssetValueType.None)
			{
				CopyValue(source.Value, target.Value);
			}
			return;
		}
		foreach (AssetTypeValueField child in target.Children)
		{
			AssetTypeValueField match = source.Children.FirstOrDefault(c => c.FieldName == child.FieldName);
			if (match != null)
			{
				Copy(match, child);
			}
		}
	}

	private static void CopyArray(AssetTypeValueField source, AssetTypeValueField target)
	{
		if (!source.TemplateField.IsArray)
		{
			return;
		}
		if (target.Value != null && target.Value.ValueType == AssetValueType.ByteArray)
		{
			if (source.Value != null && source.Value.ValueType == AssetValueType.ByteArray)
			{
				target.Value.AsByteArray = source.Value.AsByteArray;
			}
			return;
		}
		List<AssetTypeValueField> existing = target.Children;
		var elements = new List<AssetTypeValueField>();
		for (int i = 0; i < source.Children.Count; i++)
		{
			AssetTypeValueField element;
			if (i < existing.Count)
			{
				element = existing[i];
			}
			else
			{
				element = ValueBuilder.DefaultValueFieldFromArrayTemplate(target);
				if (existing.Count > 0)
				{
					Copy(existing[0], element);
				}
			}
			Copy(source.Children[i], element);
			elements.Add(element);
		}
		target.Children = elements;
		target.AsArray = new AssetTypeArrayInfo(elements.Count);
	}

	private static void CopyValue(AssetTypeValue source, AssetTypeValue target)
	{
		switch (target.ValueType)
		{
			case AssetValueType.Bool:
				target.AsBool = source.AsBool;
				break;
			case AssetValueType.Int8:
			case AssetValueType.Int16:
			case AssetValueType.Int32:
			case AssetValueType.Int64:
				target.AsLong = source.AsLong;
				break;
			case AssetValueType.UInt8:
			case AssetValueType.UInt16:
			case AssetValueType.UInt32:
			case AssetValueType.UInt64:
				target.AsULong = source.AsULong;
				break;
			case AssetValueType.Float:
			case AssetValueType.Double:
				target.AsDouble = source.AsDouble;
				break;
			case AssetValueType.String:
				target.AsString = source.AsString;
				break;
		}
	}
}

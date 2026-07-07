using Robust.Shared.Utility;

namespace Content.Shared.Dataset;

public static class DatasetLocalizationHelpers
{
    public static string GetLocalizedValue(DatasetPrototype dataset, int index)
    {
        var value = dataset.Values[index];

        if (TryGetLocalizedEntry(dataset.ID, index + 1, out var localizedValue))
            return localizedValue;

        return Loc.TryGetString(value, out localizedValue)
            ? localizedValue
            : value;
    }

    public static bool TryGetLocalizedEntry(string datasetId, int oneBasedIndex, out string localizedValue)
    {
        var locId = $"{NormalizeDatasetId(datasetId)}-dataset-{oneBasedIndex}";
        var found = Loc.TryGetString(locId, out var value);
        localizedValue = value ?? string.Empty;
        return found;
    }

    private static string NormalizeDatasetId(string datasetId)
    {
        if (datasetId.Contains('_'))
            return datasetId.Replace('_', '-');

        return CaseConversion.PascalToKebab(datasetId);
    }
}

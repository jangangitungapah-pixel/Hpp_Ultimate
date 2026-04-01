using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class DashboardState
{
    public DashboardPeriodPreset Preset { get; private set; } = DashboardPeriodPreset.ThisMonth;
    public Guid? SelectedProductId { get; private set; }
    public DateOnly? CustomFrom { get; private set; }
    public DateOnly? CustomTo { get; private set; }
    public string SearchText { get; private set; } = string.Empty;
    public string SortColumn { get; private set; } = "profit";
    public bool SortDescending { get; private set; } = true;
    public int Page { get; private set; } = 1;
    public int PageSize { get; } = 6;

    public event Action? Changed;

    public DashboardFilter CurrentFilter => new(Preset, CustomFrom, CustomTo, SelectedProductId);

    public void SetPreset(DashboardPeriodPreset preset)
    {
        Preset = preset;
        if (preset != DashboardPeriodPreset.Custom)
        {
            CustomFrom = null;
            CustomTo = null;
        }

        Page = 1;
        Changed?.Invoke();
    }

    public void SetCustomRange(DateOnly? from, DateOnly? to)
    {
        CustomFrom = from;
        CustomTo = to;
        Preset = DashboardPeriodPreset.Custom;
        Page = 1;
        Changed?.Invoke();
    }

    public void SetProduct(Guid? productId)
    {
        SelectedProductId = productId;
        Page = 1;
        Changed?.Invoke();
    }

    public void SetSearch(string? search)
    {
        SearchText = search?.Trim() ?? string.Empty;
        Page = 1;
        Changed?.Invoke();
    }

    public void SetSort(string sortColumn)
    {
        if (SortColumn.Equals(sortColumn, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = sortColumn;
            SortDescending = true;
        }

        Changed?.Invoke();
    }

    public void SetPage(int page)
    {
        Page = Math.Max(1, page);
        Changed?.Invoke();
    }
}

namespace ClubReportHub.Shared.Data;

public static class Pagination
{
    public static int Offset(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var offset = (long)(Math.Max(page, 1) - 1) * pageSize;
        return (int)Math.Min(offset, int.MaxValue);
    }
}

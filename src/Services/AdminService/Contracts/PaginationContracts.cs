using AdminService.Errors;

namespace AdminService.Contracts;

public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int MaximumPageSize = 100;

    public void Validate()
    {
        var details = new List<ErrorDetail>();
        if (Page < 1)
        {
            details.Add(new ErrorDetail("page", "Page must be at least 1."));
        }

        if (PageSize < 1 || PageSize > MaximumPageSize)
        {
            details.Add(new ErrorDetail(
                "pageSize",
                $"Page size must be between 1 and {MaximumPageSize}."));
        }

        if (details.Count > 0)
        {
            throw new RequestValidationException(details);
        }
    }
}

public sealed record PaginationMetadata(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    PaginationMetadata Pagination)
{
    public static PagedResult<T> Create(
        IReadOnlyCollection<T> items,
        int page,
        int pageSize,
        int totalItems) =>
        new(
            items,
            new PaginationMetadata(
                page,
                pageSize,
                totalItems,
                totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize)));
}

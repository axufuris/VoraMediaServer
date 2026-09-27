using Vora.Application.Streaming.Dtos;

namespace Vora.Application.Streaming.ViewModels;

// The admin history is paged, so the rows alone are not the answer — the total
// is what the pager needs. It was returned as an anonymous pair, which serializes
// correctly and tells a generated client nothing about either field.
public class StreamHistoryPageVM
{
    public required List<HistorySessionDto> Data { get; set; }
    public int Total { get; set; }
}

namespace Vora.Application.Auth.ViewModels;

public class LdapTestResultVM
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public bool Connected { get; set; }
    public bool ServiceBound { get; set; }
    public int EntriesFound { get; set; }
    public bool SearchLimited { get; set; }
    public int EntriesWithMail { get; set; }
    public int EntriesWithoutMail { get; set; }
    public int EntriesWithUniqueId { get; set; }
    public List<string> SampleDns { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

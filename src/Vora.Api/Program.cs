using Vora.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

builder.AddVoraServices();

var app = builder.Build();

app.UseVoraPipeline();
app.MapVoraEndpoints();

await app.RunVoraStartupTasksAsync();

app.Run();

public partial class Program { }

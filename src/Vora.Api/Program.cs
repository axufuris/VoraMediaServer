using Vora.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddVoraServices();

var app = builder.Build();

app.UseVoraPipeline();
app.MapVoraEndpoints();

await app.RunVoraStartupTasksAsync();

app.Run();

public partial class Program { }

using Controledu.Teacher.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Controledu.ApiContractExporter <teacher> <output-path>");
    return 1;
}

var target = args[0].Trim().ToLowerInvariant();
var outputPath = Path.GetFullPath(args[1]);

if (target != "teacher")
{
    Console.Error.WriteLine($"Unsupported target '{target}'. Expected 'teacher'.");
    return 1;
}

var outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

var builder = WebApplication.CreateBuilder([
    "--TeacherServer:HttpPort=40556",
    "--TeacherServer:DiscoveryPort=40555",
    $"--TeacherServer:StorageFile={Path.Combine(Path.GetTempPath(), "controledu-contracts", $"teacher-{Guid.NewGuid():N}.db")}",
    $"--TeacherServer:TransferRoot={Path.Combine(Path.GetTempPath(), "controledu-contracts", $"transfers-{Guid.NewGuid():N}")}",
]);
TeacherServerHostFactory.ConfigureBuilder(builder);
var app = builder.Build();

try
{
    var swaggerProvider = app.Services.GetRequiredService<ISwaggerProvider>();
    var document = swaggerProvider.GetSwagger("v1");

    await using var stream = File.Create(outputPath);
    await using var streamWriter = new StreamWriter(stream);
    var writer = new OpenApiJsonWriter(streamWriter);
    document.SerializeAsV3(writer);
    await streamWriter.FlushAsync();
}
finally
{
    await app.DisposeAsync();
}

return 0;

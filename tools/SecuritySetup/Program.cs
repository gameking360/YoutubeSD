using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Directory.GetCurrentDirectory());
if (!File.Exists(Path.Combine(root, "README.md")) || !Directory.Exists(Path.Combine(root, "Orchestrador")))
    throw new InvalidOperationException("Execute na raiz do repositório ou informe seu caminho.");
var directory = Path.Combine(root, ".secrets");
Directory.CreateDirectory(directory);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var jsonPath = Path.Combine(directory, "security.json");
Dictionary<string, string> parameters;
if (File.Exists(jsonPath))
{
    using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
    parameters = document.RootElement.GetProperty("Parameters").Deserialize<Dictionary<string, string>>()!;
    // Validate without replacing existing keys: restarting must never invalidate previously issued JWTs.
    using var existing = RSA.Create();
    existing.ImportPkcs8PrivateKey(Convert.FromBase64String(parameters["security-signing-key"]), out _);
    if (existing.KeySize < 2048 || parameters["identity-internal-key"].Length < 32 || parameters["identity-admin-token"].Length < 32)
        throw new InvalidOperationException("Configuração existente inválida; nenhum segredo foi alterado.");
}
else
{
    using var rsa = RSA.Create(3072);
    parameters = new()
    {
        ["security-signing-key"] = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()),
        ["identity-internal-key"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        ["identity-admin-token"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
    };
    // CreateNew protects an existing configuration even if two setup processes run together.
    using var file = new FileStream(jsonPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    JsonSerializer.Serialize(file, new { Parameters = parameters }, new JsonSerializerOptions { WriteIndented = true });
}
var envPath = Path.Combine(root, "deploy", ".env");
var existingEnv = File.Exists(envPath) ? File.ReadAllLines(envPath).ToList() : [];
var values = new Dictionary<string, string>
{
    ["SECURITY_SIGNING_KEY"] = parameters["security-signing-key"],
    ["IDENTITY_INTERNAL_KEY"] = parameters["identity-internal-key"],
    ["IDENTITY_ADMIN_TOKEN"] = parameters["identity-admin-token"]
};
foreach (var (name, value) in values)
{
    var line = existingEnv.FindIndex(l => l.StartsWith(name + "=", StringComparison.Ordinal));
    if (line >= 0 && existingEnv[line] != name + "=" + value)
        throw new InvalidOperationException($"{name} diverge entre deploy/.env e .secrets/security.json; nenhum valor será sobrescrito.");
    if (line < 0) existingEnv.Add(name + "=" + value);
}
File.WriteAllLines(envPath, existingEnv, new UTF8Encoding(false));
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(jsonPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    File.SetUnixFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
Console.WriteLine("Configuração local pronta: .secrets/security.json e deploy/.env. Chave e segredos existentes preservados.");

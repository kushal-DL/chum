using System.IO;
using System.Text.Json;
using Chum.Llm;

namespace Chum.App.Services;

public sealed class TemplateService
{
    private static readonly string TemplatesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chum", "templates.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private List<PromptTemplate> _userTemplates = [];

    /// <summary>All templates: built-ins first, then user-defined (no duplicates by name).</summary>
    public IReadOnlyList<PromptTemplate> All => BuildMergedList();

    public void Load()
    {
        if (!File.Exists(TemplatesPath)) return;
        try
        {
            var json = File.ReadAllText(TemplatesPath);
            var dtos = JsonSerializer.Deserialize<List<TemplateDto>>(json) ?? [];
            _userTemplates = dtos
                .Where(d => !string.IsNullOrWhiteSpace(d.Name))
                .Select(d => new PromptTemplate(d.Name!, d.SystemPromptSuffix ?? string.Empty, d.MaxTokensOverride))
                .ToList();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load prompt templates — using built-ins only");
            _userTemplates = [];
        }
    }

    public void Save(IEnumerable<PromptTemplate> userTemplates)
    {
        _userTemplates = userTemplates
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatesPath)!);
            var dtos = _userTemplates.Select(t => new TemplateDto
            {
                Name = t.Name,
                SystemPromptSuffix = t.SystemPromptSuffix,
                MaxTokensOverride = t.MaxTokensOverride
            }).ToList();
            File.WriteAllText(TemplatesPath, JsonSerializer.Serialize(dtos, JsonOpts));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to save prompt templates");
        }
    }

    public PromptTemplate? GetByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private List<PromptTemplate> BuildMergedList()
    {
        var builtInNames = new HashSet<string>(
            PromptTemplate.BuiltIns.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<PromptTemplate>(PromptTemplate.BuiltIns);
        result.AddRange(_userTemplates.Where(u => !builtInNames.Contains(u.Name)));
        return result;
    }

    private sealed class TemplateDto
    {
        public string? Name { get; set; }
        public string? SystemPromptSuffix { get; set; }
        public int? MaxTokensOverride { get; set; }
    }
}

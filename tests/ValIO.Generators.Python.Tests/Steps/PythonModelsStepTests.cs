using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.Python.Tests.TestHelpers;

namespace ValIO.Generators.Python.Tests.Steps;

/// <summary>Checks model source declarations and package-level forward-reference initialization.</summary>
[TestFixture]
public class PythonModelsStepTests
{
    /// <summary>Runs the actual Python generation pipeline for each source assertion.</summary>
    private PythonPipelineTestHarness _harness = null!;

    /// <summary>Creates the pipeline services used by the fixture.</summary>
    [OneTimeSetUp]
    public void Setup() => _harness = new PythonPipelineTestHarness();

    /// <summary>Disposes the pipeline services after the fixture completes.</summary>
    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    /// <summary>Preserves one model module per declaration using Python file naming.</summary>
    /// <returns>A task completing after generated file names have been checked.</returns>
    [Test]
    public async Task GeneratesOneFilePerModel_InSnakeCase()
    {
        var doc = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var modelFiles = ctx.GeneratedFiles
            .Where(f => f.RelativePath.Replace('\\', '/').StartsWith("models/", StringComparison.Ordinal))
            .ToList();
        modelFiles.Should().NotBeEmpty();
        modelFiles.Should().Contain(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("models/user.py", StringComparison.Ordinal));
    }

    /// <summary>Generates Pydantic declarations with consistent input naming configuration.</summary>
    /// <returns>A task completing after model configuration assertions.</returns>
    [Test]
    public async Task EmitsPydanticBaseModelWithConfig()
    {
        var doc = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var userFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("models/user.py", StringComparison.Ordinal));
        userFile.Content.Should().Contain("from pydantic import BaseModel, ConfigDict");
        userFile.Content.Should().Contain("class User(BaseModel):");
        userFile.Content.Should().Contain("model_config = ConfigDict(populate_by_name=True)");
    }

    /// <summary>Distinguishes required fields from fields whose absence is represented by an unset default.</summary>
    /// <returns>A task completing after default and presence assertions.</returns>
    [Test]
    public async Task RequiredFieldsHaveNoDefault_OptionalGetNone()
    {
        var doc = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var userFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("models/user.py", StringComparison.Ordinal));
        var normalized = userFile.Content.Replace("\r\n", "\n", StringComparison.Ordinal);
        // Required fields have no default; omission remains a validation failure.
        normalized.Should().Contain("id: int\n");
        normalized.Should().Contain("name: str\n");
        normalized.Should().Contain("email: str\n");
        // A missing optional field receives an unset default without adding schema nullability.
        normalized.Should().Contain("= None");
    }

    /// <summary>Defers enum forward-reference resolution until all package declarations are imported.</summary>
    /// <returns>A task completing after module and package initialization assertions.</returns>
    [Test]
    public async Task ForwardRefToEnum_QuotedAndImportedUnderTypeChecking()
    {
        var doc = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var userFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("models/user.py", StringComparison.Ordinal));
        userFile.Content.Should().Contain("if TYPE_CHECKING:");
        userFile.Content.Should().Contain("from .user_role import UserRole");
        userFile.Content.Should().Contain("role: \"UserRole\" = None");
        userFile.Content.Should().NotContain("User.model_rebuild()");
        var package = ctx.GeneratedFiles.Single(file => file.RelativePath == "models/__init__.py");
        package.Content.Should().Contain("from .user import User");
        package.Content.Should().Contain("from .user_role import UserRole");
        package.Content.Should().Contain("User.model_rebuild(_types_namespace=globals())");
    }

    /// <summary>Emits postponed annotation evaluation for inter-model reference cycles.</summary>
    /// <returns>A task completing after the future import assertion.</returns>
    [Test]
    public async Task IncludesFutureAnnotationsImport()
    {
        var doc = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var userFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').EndsWith("models/user.py", StringComparison.Ordinal));
        userFile.Content.Should().StartWith("from __future__ import annotations");
    }
}

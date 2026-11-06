using System.Collections.Concurrent;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Builders;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects shared declaration identity, generation ordering, and per-request context ownership.</summary>
[TestFixture]
public sealed class IrDocumentContextTests
{
    /// <summary>Ensures a missing document fails at the context boundary.</summary>
    [Test]
    public void Constructor_WithNullDocument_ThrowsArgumentNullException()
    {
        Action create = () => _ = new IrDocumentContext(null!);

        create.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("document");
    }

    /// <summary>Ensures a document without declarations is a valid explicit fragment context.</summary>
    [Test]
    public void EmptyDocument_HasEmptyDeclarationIndexesAndOrder()
    {
        var context = new IrDocumentContext(CreateDocument());

        context.ModelsByName.Should().BeEmpty();
        context.EnumsByName.Should().BeEmpty();
        context.EnumNames.Should().BeEmpty();
        context.KnownTypeNames.Should().BeEmpty();
        context.ModelsInDependencyOrder.Should().BeEmpty();
    }

    /// <summary>Ensures indexes preserve declaration objects and use generated names rather than original names.</summary>
    [Test]
    public void Indexes_PreserveOriginalDeclarationInstancesAndGeneratedNames()
    {
        var model = CreateModel("OrderDto") with { OriginalName = "Order" };
        var enumModel = CreateEnum("StatusEnum") with { OriginalName = "Status" };
        var document = CreateDocument([model], [enumModel]);

        var context = new IrDocumentContext(document);

        context.Document.Should().BeSameAs(document);
        context.ModelsByName["OrderDto"].Should().BeSameAs(model);
        context.EnumsByName["StatusEnum"].Should().BeSameAs(enumModel);
        context.ModelsByName.ContainsKey("Order").Should().BeFalse();
        context.EnumsByName.ContainsKey("Status").Should().BeFalse();
        context.EnumNames.Should().Equal("StatusEnum");
        context.KnownTypeNames.Should().BeEquivalentTo(new[] { "OrderDto", "StatusEnum" });
    }

    /// <summary>Ensures case-distinct declaration names remain distinct in every index.</summary>
    [Test]
    public void Indexes_UseOrdinalCaseSensitiveNames()
    {
        var upperModel = CreateModel("Thing");
        var lowerModel = CreateModel("thing");
        var upperEnum = CreateEnum("Mode");
        var lowerEnum = CreateEnum("mode");
        var context = new IrDocumentContext(CreateDocument([upperModel, lowerModel], [upperEnum, lowerEnum]));

        context.ModelsByName["Thing"].Should().BeSameAs(upperModel);
        context.ModelsByName["thing"].Should().BeSameAs(lowerModel);
        context.EnumsByName["Mode"].Should().BeSameAs(upperEnum);
        context.EnumsByName["mode"].Should().BeSameAs(lowerEnum);
        context.ModelsByName.ContainsKey("THING").Should().BeFalse();
        context.EnumsByName.ContainsKey("MODE").Should().BeFalse();
        context.EnumNames.Contains("MODE").Should().BeFalse();
        context.KnownTypeNames.Contains("THING").Should().BeFalse();
        context.KnownTypeNames.Should().HaveCount(4);
    }

    /// <summary>Ensures duplicate model declarations cannot silently replace one another.</summary>
    [Test]
    public void DuplicateModelNames_ThrowAnExplicitDiagnostic()
    {
        var document = CreateDocument([CreateModel("Repeated"), CreateModel("Repeated")]);
        Action create = () => _ = new IrDocumentContext(document);

        create.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate model name 'Repeated'*");
    }

    /// <summary>Ensures duplicate enum declarations cannot silently replace one another.</summary>
    [Test]
    public void DuplicateEnumNames_ThrowAnExplicitDiagnostic()
    {
        var document = CreateDocument(enums: [CreateEnum("Repeated"), CreateEnum("Repeated")]);
        Action create = () => _ = new IrDocumentContext(document);

        create.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate enum name 'Repeated'*");
    }

    /// <summary>Ensures a reference name never ambiguously identifies both a model and an enum.</summary>
    [Test]
    public void ModelAndEnumSharingAName_ThrowAnExplicitDiagnostic()
    {
        var document = CreateDocument([CreateModel("Shared")], [CreateEnum("Shared")]);
        Action create = () => _ = new IrDocumentContext(document);

        create.Should().Throw<InvalidOperationException>().WithMessage("*'Shared'*both a model and an enum*");
    }

    /// <summary>Ensures graph projection skips non-model vertices and appends omitted models in source order.</summary>
    [Test]
    public void ModelOrder_UsesGraphOrderWithDistinctSourceOrderFallback()
    {
        var child = CreateModel("Child");
        var other = CreateModel("Other");
        var baseModel = CreateModel("Base");
        var tail = CreateModel("Tail");
        string[] graphOrder = ["Mode", "Base", "Base", "Unknown", "Child"];
        var document = CreateDocument([child, other, baseModel, tail], [CreateEnum("Mode")], graphOrder);

        var context = new IrDocumentContext(document);

        context.ModelsInDependencyOrder.Select(model => model.Name).Should().Equal("Base", "Child", "Other", "Tail");
        context.ModelsInDependencyOrder[0].Should().BeSameAs(baseModel);
        context.ModelsInDependencyOrder[1].Should().BeSameAs(child);
        document.TypeDependencies.TopologicalOrder.Should().Equal(graphOrder);
        document.Models.Select(model => model.Name).Should().Equal("Child", "Other", "Base", "Tail");
    }

    /// <summary>Ensures the context follows the shared production dependency graph for referenced models and enums.</summary>
    [Test]
    public void ModelOrder_PreservesCanonicalDependencyOrdering()
    {
        var child = CreateModel("Child", [CreateReferenceProperty("Base")]);
        var baseModel = CreateModel("Base", [CreateReferenceProperty("Mode")]);
        var document = CreateDocument([child, baseModel], [CreateEnum("Mode")]);
        document = document with
        {
            TypeDependencies = new IrTypeDependencyGraphBuilder().Build(document.Models, document.Enums),
        };

        var context = new IrDocumentContext(document);

        context.ModelsInDependencyOrder.Select(model => model.Name).Should().Equal("Base", "Child");
        context.EnumNames.Should().Contain("Mode");
    }

    /// <summary>Ensures a direct builder can receive an explicit partial document without invented external declarations.</summary>
    [Test]
    public void ExplicitFragmentContext_AllowsUnresolvedExternalReferences()
    {
        var model = CreateModel("Fragment", [CreateReferenceProperty("External")]);
        var context = new IrDocumentContext(CreateDocument([model], topologicalOrder: ["External", "Fragment"]));
        var builder = new ContextProbeBuilder(context);

        builder.ModelsByName["Fragment"].Should().BeSameAs(model);
        builder.ModelsByName.ContainsKey("External").Should().BeFalse();
        context.ModelsInDependencyOrder.Should().ContainSingle().Which.Should().BeSameAs(model);
        model.Properties.Single().Type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("External");
    }

    /// <summary>Ensures document indexes cannot retain a previous request's classification for a shared name.</summary>
    [Test]
    public void IndependentDocuments_DoNotShareModelOrEnumClassification()
    {
        var model = CreateModel("Shared");
        var enumModel = CreateEnum("Shared");
        var modelContext = new IrDocumentContext(CreateDocument([model]));
        var enumContext = new IrDocumentContext(CreateDocument(enums: [enumModel]));

        modelContext.ModelsByName["Shared"].Should().BeSameAs(model);
        modelContext.EnumNames.Contains("Shared").Should().BeFalse();
        enumContext.EnumsByName["Shared"].Should().BeSameAs(enumModel);
        enumContext.ModelsByName.ContainsKey("Shared").Should().BeFalse();
    }

    /// <summary>Ensures lazy indexing observes object-initializer inputs and preserves the shared context on repeated reads.</summary>
    [Test]
    public void GenerationContext_CachesIndexesForItsInitializedDocument()
    {
        var document = CreateDocument([CreateModel("Initialized")]);
        var generationContext = CreateGenerationContext(document);

        var first = generationContext.DocumentContext;
        var second = generationContext.DocumentContext;

        first.Should().BeSameAs(second);
        first.Document.Should().BeSameAs(document);
        first.ModelsByName.Should().ContainKey("Initialized");
    }

    /// <summary>Ensures concurrent step consumers receive one cached document context.</summary>
    [Test]
    public void GenerationContext_ConcurrentReadsPublishTheSameContext()
    {
        var generationContext = CreateGenerationContext(CreateDocument([CreateModel("Concurrent")]));
        var contexts = new ConcurrentBag<IrDocumentContext>();

        Parallel.For(0, 64, _ => contexts.Add(generationContext.DocumentContext));

        var cached = generationContext.DocumentContext;
        contexts.Should().HaveCount(64).And.OnlyContain(context => ReferenceEquals(context, cached));
        cached.ModelsByName.Should().ContainKey("Concurrent");
    }

    /// <summary>Ensures two requests referencing one IR document still own separate index contexts and mutable results.</summary>
    [Test]
    public void GenerationContexts_OwnTheirIndexesAndResultsIndependently()
    {
        var document = CreateDocument([CreateModel("SharedInput")]);
        var first = CreateGenerationContext(document);
        var second = CreateGenerationContext(document);

        first.Warnings.Add("first request");
        first.StepData.Add("first", true);

        first.DocumentContext.Should().NotBeSameAs(second.DocumentContext);
        first.DocumentContext.Document.Should().BeSameAs(second.DocumentContext.Document);
        second.Warnings.Should().BeEmpty();
        second.StepData.Should().BeEmpty();
    }

    /// <summary>Ensures builders expose the shared indexes without allocating private substitutes.</summary>
    [Test]
    public void BaseBuilder_ForwardsTheExactContextAndIndexInstances()
    {
        var context = new IrDocumentContext(CreateDocument([CreateModel("Model")], [CreateEnum("Mode")]));
        var first = new ContextProbeBuilder(context);
        var second = new ContextProbeBuilder(context);

        first.Context.Should().BeSameAs(context);
        first.ModelsByName.Should().BeSameAs(context.ModelsByName).And.BeSameAs(second.ModelsByName);
        first.EnumsByName.Should().BeSameAs(context.EnumsByName).And.BeSameAs(second.EnumsByName);
        first.EnumNames.Should().BeSameAs(context.EnumNames).And.BeSameAs(second.EnumNames);
    }

    /// <summary>Ensures document-aware builders cannot be created with an implicit missing context.</summary>
    [Test]
    public void BaseBuilder_WithNullContext_ThrowsArgumentNullException()
    {
        Action create = () => _ = new ContextProbeBuilder(null!);

        create.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("documentContext");
    }

    /// <summary>Creates a small explicit IR document without parsing or generating unrelated schemas.</summary>
    /// <param name="models">The model declarations, or an empty collection.</param>
    /// <param name="enums">The enum declarations, or an empty collection.</param>
    /// <param name="topologicalOrder">The existing graph order, or an empty source-order fallback.</param>
    /// <returns>A document suitable for declaration-index and fragment-builder scenarios.</returns>
    private static IrDocument CreateDocument(
        IReadOnlyList<IrModel>? models = null,
        IReadOnlyList<IrEnum>? enums = null,
        IReadOnlyList<string>? topologicalOrder = null) => new()
    {
        Metadata = new IrDocumentMetadata(),
        Models = models ?? [],
        Enums = enums ?? [],
        Clients = [],
        TypeDependencies = new IrTypeDependencyGraph
        {
            Dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            TopologicalOrder = topologicalOrder ?? [],
        },
    };

    /// <summary>Creates a named model without changing the supplied property objects.</summary>
    /// <param name="name">The generated declaration name.</param>
    /// <param name="properties">The model properties, or an empty collection.</param>
    /// <returns>The explicit model declaration.</returns>
    private static IrModel CreateModel(string name, IReadOnlyList<IrProperty>? properties = null) => new()
    {
        Name = name,
        Properties = properties ?? [],
    };

    /// <summary>Creates a named enum for reference-classification scenarios.</summary>
    /// <param name="name">The generated declaration name.</param>
    /// <returns>An explicit string enum declaration.</returns>
    private static IrEnum CreateEnum(string name) => new()
    {
        Name = name,
        BaseType = IrEnumBaseType.String,
        Values = [],
    };

    /// <summary>Creates a required property referencing a declaration by its exact name.</summary>
    /// <param name="name">The referenced declaration name.</param>
    /// <returns>The property retaining its reference type.</returns>
    private static IrProperty CreateReferenceProperty(string name) => new()
    {
        Name = "Value",
        OriginalName = "value",
        IsRequired = true,
        Type = new IrModelReference { ModelName = name },
    };

    /// <summary>Creates a request through the existing required-property object-initializer contract.</summary>
    /// <param name="document">The finalized document belonging to this request.</param>
    /// <returns>The request context before its document indexes are first accessed.</returns>
    private static GenerationContext CreateGenerationContext(IrDocument document) => new()
    {
        IrDocument = document,
        Configuration = new GenerationConfiguration(),
    };

    /// <summary>Exposes the base context for behavior tests without introducing language-specific output.</summary>
    private sealed class ContextProbeBuilder : AstBuilderBase
    {
        /// <summary>Creates a document-aware builder with the mandatory input context.</summary>
        /// <param name="documentContext">The explicit document context supplied by the caller.</param>
        /// <exception cref="ArgumentNullException"><paramref name="documentContext"/> is null.</exception>
        public ContextProbeBuilder(IrDocumentContext documentContext) : base(documentContext)
        {
        }

        /// <summary>Gets the context retained by the shared builder base.</summary>
        /// <value>The exact context supplied at construction.</value>
        public IrDocumentContext Context => DocumentContext;
    }
}

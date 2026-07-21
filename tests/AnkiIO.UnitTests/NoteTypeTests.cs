using Xunit;

namespace AnkiIO.UnitTests;

public sealed class NoteTypeTests
{
    [Fact]
    public void ConfiguredFieldMethodPreservesEditorMetadataAndFluentResult()
    {
        var type = new AnkiNoteType("Language");
        var field = new AnkiField(
            "Arabic",
            IsRightToLeft: true,
            IsSticky: true,
            Font: "Noto Sans Arabic",
            FontSize: 24);

        var result = type.AddConfiguredField(field);

        Assert.Same(type, result);
        Assert.Same(field, Assert.Single(type.Fields));
        Assert.True(type.Fields[0].IsRightToLeft);
        Assert.True(type.Fields[0].IsSticky);
        Assert.Equal("Noto Sans Arabic", type.Fields[0].Font);
        Assert.Equal(24, type.Fields[0].FontSize);
    }

    [Fact]
    public void ConfiguredFieldMethodRejectsInvalidOrDuplicateDefinitions()
    {
        var type = new AnkiNoteType("Validated").AddField("Front");

        Assert.Throws<ArgumentNullException>(() => type.AddConfiguredField(null!));
        Assert.Throws<ArgumentException>(() => type.AddConfiguredField(new AnkiField(" ")));
        Assert.Throws<ArgumentException>(() => type.AddConfiguredField(new AnkiField("Back", Font: " ")));
        Assert.Throws<ArgumentOutOfRangeException>(() => type.AddConfiguredField(new AnkiField("Back", FontSize: 0)));
        Assert.Throws<ArgumentException>(() => type.AddConfiguredField(new AnkiField("front")));
        Assert.Single(type.Fields);
    }

    [Fact]
    public void ConfiguredTemplateMethodPreservesBrowserFormatsAndFluentResult()
    {
        var type = new AnkiNoteType("Language").AddField("Word").AddField("Meaning");
        var template = new AnkiCardTemplate(
            "Recognition",
            "{{Word}}",
            "{{Meaning}}",
            BrowserQuestionFormat: "Q: {{Word}}",
            BrowserAnswerFormat: "A: {{Meaning}}");

        var result = type.AddConfiguredTemplate(template);

        Assert.Same(type, result);
        Assert.Same(template, Assert.Single(type.Templates));
        Assert.Equal("Q: {{Word}}", type.Templates[0].BrowserQuestionFormat);
        Assert.Equal("A: {{Meaning}}", type.Templates[0].BrowserAnswerFormat);
    }

    [Fact]
    public void ConfiguredTemplateMethodRejectsInvalidOrDuplicateDefinitions()
    {
        var type = new AnkiNoteType("Validated").AddTemplate("Card", "question", "answer");

        Assert.Throws<ArgumentNullException>(() => type.AddConfiguredTemplate(null!));
        Assert.Throws<ArgumentException>(() => type.AddConfiguredTemplate(new AnkiCardTemplate(" ", "question", "answer")));
        Assert.Throws<ArgumentNullException>(() => type.AddConfiguredTemplate(new AnkiCardTemplate("Other", null!, "answer")));
        Assert.Throws<ArgumentNullException>(() => type.AddConfiguredTemplate(new AnkiCardTemplate("Other", "question", null!)));
        Assert.Throws<ArgumentException>(() => type.AddConfiguredTemplate(new AnkiCardTemplate("card", "question", "answer")));
        Assert.Single(type.Templates);
    }

    [Fact]
    public void ExistingStringMethodsKeepNullBindingAndDuplicateParameterNames()
    {
        var type = new AnkiNoteType("Compatibility")
            .AddField("Front")
            .AddTemplate("Card", "{{Front}}", "{{Front}}");

        var nullField = Assert.Throws<ArgumentNullException>(() => type.AddField(null!));
        var duplicateField = Assert.Throws<ArgumentException>(() => type.AddField("front"));
        var duplicateTemplate = Assert.Throws<ArgumentException>(() => type.AddTemplate("card", "question", "answer"));

        Assert.Equal("name", nullField.ParamName);
        Assert.Equal("name", duplicateField.ParamName);
        Assert.Equal("name", duplicateTemplate.ParamName);
    }

    [Fact]
    public void StableIdentifierUsesDocumentedCrossPlatformProjection()
    {
        Assert.Equal(2_532_159_573_538_256_489, AnkiId.FromStableValue("external-note", "abc"));
    }

    [Fact]
    public void ExplicitlyDocumentedRecordPropertiesPreservePositionalRecordBehavior()
    {
        var field = new AnkiField("Front", IsRightToLeft: true, IsSticky: true, Font: "Arial", FontSize: 21);
        var (fieldName, rightToLeft, sticky, font, fontSize) = field;
        Assert.Equal(("Front", true, true, "Arial", 21), (fieldName, rightToLeft, sticky, font, fontSize));
        Assert.Equal(new AnkiField("Front", true, true, "Arial", 21), field);

        var template = new AnkiCardTemplate("Card", "question", "answer", "browser question", "browser answer");
        var (templateName, question, answer, browserQuestion, browserAnswer) = template;
        Assert.Equal(("Card", "question", "answer", "browser question", "browser answer"), (templateName, question, answer, browserQuestion, browserAnswer));
        Assert.Equal(new AnkiCardTemplate("Card", "question", "answer", "browser question", "browser answer"), template);

        var diagnostic = new AnkiDiagnostic(AnkiDiagnosticSeverity.Warning, "CODE", "message", DeckId: 42);
        var (severity, code, message, _, deckId, _, _, _, _, _) = diagnostic;
        Assert.Equal((AnkiDiagnosticSeverity.Warning, "CODE", "message", 42L), (severity, code, message, deckId));
        Assert.Equal(new AnkiDiagnostic(AnkiDiagnosticSeverity.Warning, "CODE", "message", DeckId: 42), diagnostic);

        var reviewedAt = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var review = new AnkiReviewLog(100, reviewedAt, 3, 10, 5, 2500, TimeSpan.FromSeconds(2), 1);
        var (id, timestamp, ease, interval, previousInterval, easeFactor, answerTime, reviewType) = review;
        Assert.Equal((100L, reviewedAt, 3, 10, 5, 2500, TimeSpan.FromSeconds(2), 1), (id, timestamp, ease, interval, previousInterval, easeFactor, answerTime, reviewType));
        Assert.Equal(new AnkiReviewLog(100, reviewedAt, 3, 10, 5, 2500, TimeSpan.FromSeconds(2), 1), review);
    }
}

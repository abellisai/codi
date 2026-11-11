using System.Text.Json;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Conversione fra la definizione didattica (§13.2) e il documento JSON che la rappresenta. Sta
/// nel livello applicativo e non nel dominio perché è un formato di scambio e di persistenza, non
/// un'invariante: il dominio conosce <see cref="ExerciseDefinition"/>, non la sua scrittura.
///
/// Il documento è un archivio: la definizione si scrive e si rilegge intera, e il formato futuro
/// di uno schema più nuovo viene rifiutato e non interpretato come questo (§13.3). I rifiuti hanno
/// due codici distinti — il corpo non ha la forma attesa (<c>InvalidPayload</c>) oppure la forma è
/// giusta ma una regola non è soddisfatta (<c>ValidationFailed</c>) — così chi legge la risposta sa
/// se ha sbagliato il documento o i valori.
/// </summary>
public static class ExerciseDefinitionJson
{
    /// <summary>Limite del documento di definizione; scelta locale, non un requisito della specifica.</summary>
    public const int MaxDefinitionLength = 65_536;

    private static readonly JsonSerializerOptions Document = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,

        // Un campo che questa versione non conosce è un formato più nuovo, non un dettaglio da
        // ignorare: scartarlo silenziosamente significherebbe salvare una definizione diversa da
        // quella ricevuta (§13.3).
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

        // Il documento ha una sezione per ogni tipo di esercizio, e un tipo ne usa una sola: le
        // sezioni che non lo riguardano non si scrivono affatto. Senza questo, il documento di un
        // esercizio di percorso porterebbe `"quiz":null`, e la scrittura canonica — su cui si
        // calcola l'impronta di una versione pubblicata (§13.8) — cambierebbe a ogni tipo nuovo.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Legge una definizione dal documento e la restituisce validata. Il nome della richiesta
    /// serve solo a comporre il messaggio dell'errore di formato.
    /// </summary>
    public static ExerciseDefinition Parse(string? json, string requestName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, "Manca la definizione dell'esercizio.");
        }

        if (json.Length > MaxDefinitionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La definizione supera i {MaxDefinitionLength} caratteri.");
        }

        DefinitionDocument document;
        try
        {
            document = JsonSerializer.Deserialize<DefinitionDocument>(json, Document)
                ?? throw new JsonException("La definizione è vuota.");
        }
        catch (JsonException failure)
        {
            throw CodiRequestException.InvalidPayload(requestName, failure);
        }
        catch (NotSupportedException failure)
        {
            throw CodiRequestException.InvalidPayload(requestName, failure);
        }

        var toolbox = Require(document.Toolbox, "toolbox");
        var limits = Require(document.Limits, "limits");

        try
        {
            // La versione del motore sceglie le regole: schema e tipo dichiarati devono concordare
            // con essa, e i dati del tipo si compongono secondo la sua descrizione.
            var engine = ExerciseEngineRegistry.Require(document.EngineVersion, "engineVersion");

            if (document.SchemaVersion != engine.SchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    "schemaVersion", document.SchemaVersion,
                    $"Lo schema {document.SchemaVersion} non è interpretabile da questa versione, che conosce lo schema {engine.SchemaVersion}.");
            }

            return ExerciseDefinition.Create(
                engine,
                document.ExerciseType ?? string.Empty,
                document.Title ?? string.Empty,
                document.Instructions ?? string.Empty,
                toolbox.AllowedBlocks,
                ExerciseLimits.Create(
                    limits.MaxBlocks, limits.MaxNestingDepth, limits.MaxExecutionSteps, limits.MaxActiveDurationMs),
                ReadData(engine, document));
        }
        catch (ArgumentException failure)
        {
            // Le eccezioni del dominio nominano il parametro per chi legge il codice; qui il
            // messaggio va a chi usa la pagina, quindi il riferimento al parametro si toglie.
            throw new CodiRequestException(CodiErrorCodes.ValidationFailed, Describe(failure));
        }
    }

    /// <summary>
    /// Compone i dati propri del tipo dalle sezioni del documento. Quali sezioni siano obbligatorie
    /// lo sa la versione del motore: la griglia ha una scena e un completamento, l'esercizio
    /// dichiarativo ha una rappresentazione e una domanda, e chiedere all'uno le sezioni dell'altro
    /// rifiuterebbe un documento valido.
    /// </summary>
    private static IExerciseTypeData ReadData(
        IExerciseEngineDescriptor engine,
        DefinitionDocument document)
    {
        if (string.Equals(engine.ExerciseType, QuizEngine.ExerciseType, StringComparison.Ordinal))
        {
            var quiz = Require(document.Quiz, "quiz");
            var feedback = Require(document.Feedback, "feedback");

            return engine.CreateData(new QuizDefinitionParts(
                quiz.Stimulus ?? string.Empty,
                quiz.Prompt ?? string.Empty,
                ParseAnswerMatch(quiz.Match),
                ExerciseFeedback.Create(feedback.Success ?? string.Empty, feedback.Collision ?? string.Empty)));
        }

        if (string.Equals(engine.ExerciseType, ValueProgramEngine.ExerciseType, StringComparison.Ordinal))
        {
            // Un esercizio che conserva valori non ha scena né traguardo: ha variabili da dichiarare e
            // casi di prova da superare. Chiedergli la scena del percorso rifiuterebbe un documento
            // valido, ed è il motivo per cui la scelta sta qui e non nel formato.
            var dichiarate = Require(document.Variables, "variables");
            var casi = Require(document.TestCases, "testCases");
            var programFeedback = Require(document.Feedback, "feedback");

            return engine.CreateData(new ValueProgramParts(
                [.. dichiarate.Select(variable => new ValueVariable(
                    variable.Name ?? string.Empty, variable.InitialValue))],
                [.. casi.Select(caso => new ValueTestCase(
                    caso.Inputs ?? [], caso.ExpectedOutput ?? []))],
                ExerciseFeedback.Create(
                    programFeedback.Success ?? string.Empty, programFeedback.Collision ?? string.Empty)));
        }

        var scene = Require(document.Scene, "scene");
        var start = Require(scene.Start, "scene.start");
        var goal = Require(scene.Goal, "scene.goal");
        var completion = Require(document.Completion, "completion");
        var gridFeedback = Require(document.Feedback, "feedback");

        return engine.CreateData(new ExerciseDefinitionParts(
            ExerciseScene.Create(
                scene.Width,
                scene.Height,
                new GridPosition(start.X, start.Y),
                ParseDirection(start.Direction),
                [.. (scene.Obstacles ?? []).Select(obstacle => new GridPosition(obstacle.X, obstacle.Y))],
                new GridPosition(goal.X, goal.Y)),
            ExerciseCompletion.Create(
                ParseCompletionKind(completion.Type), new GridPosition(completion.X, completion.Y)),
            ExerciseFeedback.Create(gridFeedback.Success ?? string.Empty, gridFeedback.Collision ?? string.Empty)));
    }

    /// <summary>
    /// Scrive la definizione nel documento di §13.2. La scrittura è deterministica — stesso
    /// valore, stesso testo — perché il documento viene anche improntato per riconoscere una
    /// versione pubblicata identica (§13.8).
    ///
    /// La parte generale del documento è la stessa per ogni tipo; la parte propria del tipo la
    /// scrive la versione del motore, che è l'unica a sapere quali dati porta. Finché la versione
    /// registrata è una sola, il ramo è uno solo: il registro rende esplicito che cosa manca quando
    /// se ne aggiunge una seconda, invece di lasciarlo scoprire a chi pubblica.
    /// </summary>
    public static string ToJson(ExerciseDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var document = new DefinitionDocument
        {
            SchemaVersion = definition.SchemaVersion,
            ExerciseType = definition.ExerciseType,
            EngineVersion = definition.EngineVersion,
            Title = definition.Title,
            Instructions = definition.Instructions,
            Toolbox = new ToolboxDocument { AllowedBlocks = [.. definition.AllowedBlocks] },
            Limits = new LimitsDocument
            {
                MaxBlocks = definition.Limits.MaxBlocks,
                MaxNestingDepth = definition.Limits.MaxNestingDepth,
                MaxExecutionSteps = definition.Limits.MaxExecutionSteps,
                MaxActiveDurationMs = definition.Limits.MaxActiveDurationMs,
            },
        };

        WriteTypeData(definition, document);

        return JsonSerializer.Serialize(document, Document);
    }

    /// <summary>
    /// Compone la parte del documento propria del tipo dichiarato. Un tipo senza scrittura è un
    /// errore di programmazione e non un documento incompleto: pubblicare una versione che il
    /// formato non sa descrivere distribuirebbe una definizione che nessuno può rileggere.
    /// </summary>
    private static void WriteTypeData(ExerciseDefinition definition, DefinitionDocument document)
    {
        if (definition.Data is QuizData quiz)
        {
            document.Feedback = new FeedbackDocument
            {
                Success = quiz.Feedback.Success,
                Collision = quiz.Feedback.Collision,
            };
            document.Quiz = new QuizDocument
            {
                Stimulus = quiz.Stimulus,
                Prompt = quiz.Prompt,
                Match = quiz.Match.ToString(),
            };

            return;
        }

        if (definition.Data is ValueProgramData program)
        {
            document.Feedback = new FeedbackDocument
            {
                Success = program.Feedback.Success,
                Collision = program.Feedback.Collision,
            };
            document.Variables =
            [
                .. program.Variables.Select(variable => new VariableDocument
                {
                    Name = variable.Name,
                    InitialValue = variable.InitialValue,
                }),
            ];
            document.TestCases =
            [
                .. program.TestCases.Select(caso => new TestCaseDocument
                {
                    Inputs = [.. caso.Inputs],
                    ExpectedOutput = [.. caso.ExpectedOutput],
                }),
            ];

            return;
        }

        if (definition.Data is not GridPathData gridPath)
        {
            throw new InvalidOperationException(
                $"La versione «{definition.EngineVersion}» non ha una scrittura del documento: la definizione non può essere pubblicata.");
        }

        document.Scene = new SceneDocument
        {
            Width = gridPath.Scene.Width,
            Height = gridPath.Scene.Height,
            Start = new StartDocument
            {
                X = gridPath.Scene.Start.X,
                Y = gridPath.Scene.Start.Y,
                Direction = gridPath.Scene.StartDirection.ToString(),
            },
            Obstacles = [.. gridPath.Scene.Obstacles.Select(cell => new CellDocument { X = cell.X, Y = cell.Y })],
            Goal = new CellDocument { X = gridPath.Scene.Goal.X, Y = gridPath.Scene.Goal.Y },
        };
        document.Completion = new CompletionDocument
        {
            Type = gridPath.Completion.Kind.ToString(),
            X = gridPath.Completion.Target.X,
            Y = gridPath.Completion.Target.Y,
        };
        document.Feedback = new FeedbackDocument
        {
            Success = gridPath.Feedback.Success,
            Collision = gridPath.Feedback.Collision,
        };
    }

    private static T Require<T>(T? value, string name)
        where T : class =>
        value ?? throw new CodiRequestException(
            CodiErrorCodes.ValidationFailed, $"La definizione è incompleta: manca «{name}».");

    private static GridDirection ParseDirection(string? text) =>
        Enum.TryParse<GridDirection>(text, ignoreCase: true, out var direction) && Enum.IsDefined(direction)
            ? direction
            : throw new ArgumentException(
                $"L'orientamento di partenza «{text}» non è uno dei quattro della griglia (North, East, South, West).");

    private static ExerciseCompletionKind ParseCompletionKind(string? text) =>
        Enum.TryParse<ExerciseCompletionKind>(text, ignoreCase: true, out var kind) && Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentException(
                $"La condizione di completamento «{text}» non è prevista (ReachCell).");

    /// <summary>
    /// Modo di confronto della risposta di un esercizio dichiarativo. La scelta è scritta in modo
    /// leggibile nel documento (<c>Text</c>, <c>Integer</c>) e non come numero: un documento si legge
    /// anche senza il codice che lo ha prodotto.
    /// </summary>
    private static QuizAnswerMatch ParseAnswerMatch(string? text) =>
        Enum.TryParse<QuizAnswerMatch>(text, ignoreCase: true, out var match) && Enum.IsDefined(match)
            ? match
            : throw new ArgumentException(
                $"Il modo di confronto della risposta «{text}» non è previsto ({nameof(QuizAnswerMatch.Text)}, {nameof(QuizAnswerMatch.Integer)}).");

    private static string Describe(ArgumentException failure)
    {
        var marker = failure.Message.IndexOf(" (Parameter", StringComparison.Ordinal);

        return marker > 0 ? failure.Message[..marker] : failure.Message;
    }

    /// <summary>
    /// Documento di §13.2. Le proprietà generali valgono per ogni tipo di esercizio; le altre
    /// portano i dati propri di un tipo — `scene`, `completion` e `feedback` per `GridPath`,
    /// `quiz` per il tipo dichiarativo. Le proprietà sono annullabili perché il JSON può ometterle,
    /// e un documento incompleto va spiegato invece che interpretato.
    /// </summary>
    private sealed class DefinitionDocument
    {
        public int SchemaVersion { get; set; }
        public string? ExerciseType { get; set; }
        public string? EngineVersion { get; set; }
        public string? Title { get; set; }
        public string? Instructions { get; set; }
        public SceneDocument? Scene { get; set; }
        public ToolboxDocument? Toolbox { get; set; }
        public LimitsDocument? Limits { get; set; }
        public CompletionDocument? Completion { get; set; }
        public FeedbackDocument? Feedback { get; set; }
        public QuizDocument? Quiz { get; set; }

        /// <summary>Variabili dichiarate, per un esercizio che conserva valori.</summary>
        public IReadOnlyList<VariableDocument>? Variables { get; set; }

        /// <summary>Casi di prova, per un esercizio che conserva valori: prendono il posto della scena.</summary>
        public IReadOnlyList<TestCaseDocument>? TestCases { get; set; }
    }

    /// <summary>Una variabile dichiarata dalla definizione, con il suo valore di partenza.</summary>
    private sealed class VariableDocument
    {
        public string? Name { get; set; }
        public int InitialValue { get; set; }
    }

    /// <summary>
    /// Un caso di prova: i valori che il programma legge e l'uscita che deve produrre. È il traguardo
    /// di un esercizio che conserva valori, e per questo se ne dichiara più d'uno.
    /// </summary>
    private sealed class TestCaseDocument
    {
        public IReadOnlyList<int>? Inputs { get; set; }
        public IReadOnlyList<string>? ExpectedOutput { get; set; }
    }

    /// <summary>Rappresentazione, domanda e modo di confronto di un esercizio dichiarativo.</summary>
    private sealed class QuizDocument
    {
        public string? Stimulus { get; set; }
        public string? Prompt { get; set; }
        public string? Match { get; set; }
    }

    private sealed class SceneDocument
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public StartDocument? Start { get; set; }
        public IReadOnlyList<CellDocument>? Obstacles { get; set; }
        public CellDocument? Goal { get; set; }
    }

    private sealed class StartDocument
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string? Direction { get; set; }
    }

    private sealed class CellDocument
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class ToolboxDocument
    {
        public IReadOnlyList<string>? AllowedBlocks { get; set; }
    }

    private sealed class LimitsDocument
    {
        public long MaxBlocks { get; set; }
        public long MaxNestingDepth { get; set; }
        public long MaxExecutionSteps { get; set; }
        public long MaxActiveDurationMs { get; set; }
    }

    private sealed class CompletionDocument
    {
        public string? Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class FeedbackDocument
    {
        public string? Success { get; set; }
        public string? Collision { get; set; }
    }
}

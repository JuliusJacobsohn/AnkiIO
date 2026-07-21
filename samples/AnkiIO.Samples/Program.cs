using AnkiIO.Samples;

var scenarios = new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
{
    ["1"] = Scenarios.CreateBasicDeckAsync,
    ["2"] = Scenarios.NestedDecksAsync,
    ["3"] = Scenarios.InsertNotesAsync,
    ["4"] = Scenarios.InspectCardsAsync,
    ["5"] = Scenarios.ReversedAsync,
    ["6"] = Scenarios.ClozeAsync,
    ["7"] = Scenarios.ImagesAsync,
    ["8"] = Scenarios.AudioAsync,
    ["9"] = Scenarios.CustomTypeAsync,
    ["10"] = Scenarios.ImportJsonAsync,
    ["11"] = Scenarios.ExportCrowdAnkiAsync,
    ["12"] = Scenarios.ReadPackageAsync,
    ["13"] = Scenarios.ModifyPackageAsync,
    ["14"] = Scenarios.PreserveSchedulingAsync,
    ["15"] = Scenarios.ExplicitSchedulingAsync,
    ["16"] = Scenarios.NewSchedulingAsync,
    ["17"] = Scenarios.ValidateAsync,
    ["18"] = Scenarios.InspectOnlyAsync,
    ["19"] = Scenarios.MigrateAsync,
    ["20"] = Scenarios.LocalCompatibilityAsync,
};

if (args.Length == 0 || !scenarios.TryGetValue(args[0], out var run))
{
    Console.Error.WriteLine("Choose scenario 1-20. See samples/README.md.");
    return 2;
}

await run(args[1..]);
return 0;


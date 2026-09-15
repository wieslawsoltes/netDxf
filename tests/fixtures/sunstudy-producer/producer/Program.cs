using IxMilia.Dxf;
using IxMilia.Dxf.Objects;
using System.Security.Cryptography;
using System.Text.Json;

// Preserve successful and failed producer attempts without repairing the producer.
// No result here asserts native AutoCAD compatibility.
string output = Path.GetFullPath(args.Length == 0 ? "attempts" : args[0]);
Directory.CreateDirectory(output);
var results = new List<object>();
bool unexpected = false;
foreach (int year in new[] { 2013, 2018 })
foreach (bool ascii in new[] { true, false })
foreach (bool dates in new[] { false, true })
foreach (bool hours in new[] { false, true })
{
    string name = $"ixmilia-sunstudy-R{year}-{(ascii ? "ascii" : "binary")}-{(dates ? "dates" : "no-dates")}-{(hours ? "hours" : "no-hours")}.dxf";
    string path = Path.Combine(output, name);
    string expectedStage = dates ? "save" : !ascii && hours ? "reload" : "complete";
    string stage = "construct";
    bool saved = false, reloaded = false;
    try
    {
        var file = new DxfFile();
        file.Header.Version = Enum.Parse<DxfAcadVersion>("R" + year);
        var view = new DxfView("SUN_VIEW") { ViewHeight = 40, ViewWidth = 60 };
        var text = new DxfStyle("SUN_LABEL") { PrimaryFontFileName = "Arial.ttf" };
        var page = new DxfPlotSettings("SUN_PAGE") { PaperWidth = 297, PaperHeight = 210 };
        var visual = new DxfVisualStyle { Description = "SUN_VISUAL" };
        file.Views.Add(view);
        file.Styles.Add(text);
        var study = new DxfSunStudy
        {
            Version = 0, SunSetupName = "Stored solar study", Description = "Producer storage probe",
            OutputType = 0, SheetSetName = "SHEET_SET", UseSubset = true, SheetSubsetName = "SHEET_SUBSET",
            SelectDatesFromCalendar = dates, SelectRangeOfDates = !dates,
            StartTime_SecondsPastMidnight = 28800, EndTime_SecondsPastMidnight = 64800,
            IntervalInSeconds = 3600, ShadePlotType = 2, ViewportsPerPage = 6,
            ViewportDistributionRowCount = 2, ViewportDistributionColumnCount = 3,
            Spacing = 2.5, LockViewports = true, LabelViewports = true,
            PageSetupWizard = page, View = view, VisualStyle = visual, TextStyle = text,
        };
        if (dates)
        {
            study.Dates.Add(new DateTime(2024, 6, 21, 9, 30, 0));
            study.Dates.Add(new DateTime(2024, 12, 21, 15, 45, 30));
            study.Dates.Add(new DateTime(2024, 6, 21, 9, 30, 0));
        }
        if (hours) foreach (int hour in new[] { 0, 1, 0, 1 }) study.Hours.Add(hour);
        file.Objects.Add(page);
        file.Objects.Add(visual);
        file.Objects.Add(study);
        file.NamedObjectDictionary.Add("SUN_PAGE", page);
        file.NamedObjectDictionary.Add("SUN_VISUAL", visual);
        file.NamedObjectDictionary.Add("SUN_STUDY", study);

        stage = "save";
        file.Save(path, ascii);
        saved = true;
        stage = "reload";
        var loaded = DxfFile.Load(path);
        var copy = loaded.Objects.OfType<DxfSunStudy>().Single();
        reloaded = true;
        bool identity = ReferenceEquals(copy.PageSetupWizard, loaded.Objects.OfType<DxfPlotSettings>().Single())
            && ReferenceEquals(copy.View, loaded.Views.Single(v => v.Name == "SUN_VIEW"))
            && ReferenceEquals(copy.VisualStyle, loaded.Objects.OfType<DxfVisualStyle>().Single())
            && ReferenceEquals(copy.TextStyle, loaded.Styles.Single(v => v.Name == "SUN_LABEL"));
        bool datesMatch = copy.Dates.SequenceEqual(study.Dates);
        bool hoursMatch = copy.Hours.SequenceEqual(study.Hours);
        if (!identity || !datesMatch || !hoursMatch) throw new InvalidDataException("Producer changed values or reference identity.");
        stage = "resave";
        loaded.Save(Path.Combine(output, name.Replace(".dxf", "-resaved.dxf")), ascii);
        stage = "complete";
        unexpected |= expectedStage != stage;
        results.Add(new { name, expectedStage, stage, saved, reloaded, identity, datesMatch, hoursMatch });
    }
    catch (Exception error)
    {
        bool expectedFailure = error is InvalidCastException && stage == expectedStage;
        unexpected |= !expectedFailure;
        results.Add(new { name, expectedStage, stage, saved, reloaded, expectedFailure,
            errorType = error.GetType().FullName, errorMessage = error.Message });
    }
    Console.WriteLine($"{name}: {stage} (expected {expectedStage})");
}
string assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfSunStudy).Assembly.Location))).ToLowerInvariant();
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{
    producer = "IxMilia.Dxf 0.8.4", assemblySha256 = assemblyHash, unexpected, attempts = results,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return unexpected ? 1 : 0;

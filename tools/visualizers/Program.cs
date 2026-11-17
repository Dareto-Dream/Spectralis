using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Spectralis.App.Controls;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Visualizers;

// Run from the repository root. PNGs are direct captures of the shipping
// renderer, using a deterministic audio passage rather than illustrated art.
AppBuilder.Configure<CaptureApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
AppThemeService.Apply(new AppSettings());
AppMotionService.Apply(false);
var capture= !args.Contains("--review");
var target=capture?"Spectralis.App/Assets/VisualizerCaptures":"output/world-review";
Directory.CreateDirectory(target);
var frameWidth=capture?640:1280;
var frameHeight=capture?360:720;
foreach(var definition in VisualizerCatalog.All)
{
    var view=new CaptureView(definition.Mode){Width=frameWidth,Height=frameHeight};
    var window=new Window{Content=view,Width=frameWidth,Height=frameHeight};window.Show();Dispatcher.UIThread.RunJobs();
    using var bitmap=new RenderTargetBitmap(new PixelSize(frameWidth,frameHeight));bitmap.Render(view);
    bitmap.Save(Path.Combine(target,$"{definition.Mode}.png"));
    if(!capture&&definition.Mode is VisualizerMode.SignalCity or VisualizerMode.NeonTerrain or VisualizerMode.Stardust or VisualizerMode.Silk or VisualizerMode.Orbital)
    {
        var timings=new List<double>();
        for(var i=0;i<45;i++)
        {
            view.Advance();var clock=System.Diagnostics.Stopwatch.StartNew();bitmap.Render(view);timings.Add(clock.Elapsed.TotalMilliseconds);
        }
        timings.Sort();Console.WriteLine($"{definition.Label}: software drawing median {timings[22]:F2}ms, p95 {timings[42]:F2}ms");
        bitmap.Save(Path.Combine(target,$"{definition.Mode}-later.png"));
    }
    window.Close();
}
if(!capture)
foreach(var width in new[]{980,720})
{
    var options=VisualizerCatalog.All.Select(d=>new VisualizerOption(d.Label,d.Mode)).ToArray();
    var window=new VisualizerPickerWindow{Width=width,Height=720,DataContext=new VisualizerGalleryViewModel(options)};
    window.Show();
    for(var settle=0;settle<3;settle++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(30);}
    Dispatcher.UIThread.RunJobs();
    using var bitmap=new RenderTargetBitmap(new PixelSize(width,720));bitmap.Render((Control)window.Content!);bitmap.Render((Control)window.Content!);bitmap.Save($"{target}/gallery-{width}.png");window.Close();
}
Console.WriteLine($"Captured {VisualizerCatalog.All.Count} actual renderer frames in {target}.");
if(capture)
{
    var manifest=new
    {
        source="Direct Avalonia RenderTargetBitmap capture of the shipping renderer; no generated or stock artwork.",
        command="dotnet run --project tools/visualizers/CaptureVisualizers.csproj -c Release",
        width=frameWidth,height=frameHeight,frames=240,framesPerSecond=30,
        appearance=new{showHud=false,detail="Balanced",colorway="Theme"},
        sourceHashes=Directory.GetFiles("Spectralis.Core/Visualizers","*.cs",SearchOption.AllDirectories)
            .OrderBy(p=>p).ToDictionary(p=>p.Replace('\\','/'),p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))),
        images=VisualizerCatalog.All.ToDictionary(d=>$"{d.Mode}.png",d=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(target,$"{d.Mode}.png")))))
    };
    File.WriteAllText(Path.Combine(target,"captures.json"),System.Text.Json.JsonSerializer.Serialize(manifest,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
}

sealed class CaptureView:Control
{
    readonly VisualizerMode _mode;
    readonly IVisualizerRenderer _renderer;
    readonly VisualizerSceneState _state=new(){Appearance=new(){ShowHud=false,Detail=VisualizerDetail.Balanced}};
    double _time;
    public CaptureView(VisualizerMode mode){_mode=mode;_renderer=VisualizerCatalog.CreateRenderer(mode);for(var i=0;i<240;i++)Advance();}
    public void Advance(){_time+=1.0/30;_state.UpdateFrame(VisualizerDemoSource.Sample(_time,_mode==VisualizerMode.Spectrogram),true,(float)_time,_mode,1f/30);}
    public override void Render(DrawingContext context)
    {
        using var clip=context.PushClip(new Rect(Bounds.Size));
        _renderer.Draw(new AvaloniaVizCanvas(context),new(0,0,(float)Bounds.Width,(float)Bounds.Height),_state.CreateScene(VisualizerCatalog.GetDefinition(_mode).Label));
    }
}
sealed class CaptureApplication:Application
{
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Spectralis.App/")){Source=new Uri("avares://Spectralis.App/Design/Tokens.axaml")});
        Styles.Add(new SimpleTheme());Styles.Add(new StyleInclude(new Uri("avares://Spectralis.App/")){Source=new Uri("avares://Spectralis.App/Design/Controls.axaml")});
    }
}

using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class SignalCityRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        canvas.PushClipRect(bounds);
        var time = (float)scene.MotionTimeSeconds;
        var energy = scene.MusicalEnergy;
        var blocks = Detail(scene, 8, 12, 16);
        var travel = scene.TravelDistance;
        var progress = Fraction(travel * .48);
        var blockOffset = (int)Math.Floor(travel * .48);
        // A long, smooth bend; no kick-driven camera shake. Sustained energy
        // opens the avenue while transient response stays on its surfaces.
        var bend = MathF.Sin(time * .075f) * (.22f + scene.MidEnvelope * .3f);
        Vector2 View(float x, float y, float z) => Project(new(x + bend*z*.11f,
            y + .72f + energy*.08f, z), bounds, .9f);

        Span<Vector2> polygon = stackalloc Vector2[4];
        polygon[0] = View(-2, .35f, .65f); polygon[1] = View(2, .35f, .65f);
        polygon[2] = View(2, .35f, 30); polygon[3] = View(-2, .35f, 30);
        canvas.FillPolygon(polygon, VizColor.FromRgb(7, 14, 23));
        // Moving road lights retain a short spatial afterimage; audio controls
        // their brightness and pulse, not the existence or height of towers.
        for (var lane = -1; lane <= 1; lane++)
        for (var step = 0; step < 14; step++)
        {
            var z = .75f + Fraction(step/14.0 - travel*.16 - lane*.07) * 26;
            var pulse = MathF.Exp(-MathF.Abs(z - Fraction(travel*.22)*26) * .5f);
            var light = Color(scene, lane < 0 ? .15f : .85f, (int)(55+scene.BassEnvelope*90+pulse*70));
            var tail = View(lane*.55f, .32f, z+.8f+energy*.8f);
            var head = View(lane*.55f, .32f, z);
            canvas.DrawLine(tail,head,light,Math.Clamp(5/z, .6f, 3));
            if(step%3==0) canvas.DrawLine(View(-1.6f,.34f,z),View(1.6f,.34f,z),light.WithAlpha((int)(15+pulse*65)),.8f);
        }
        // Painter's order: far blocks first, with front and inward-facing wall
        // polygons so the city is a volume rather than a wall of vertical bars.
        for (var row = blocks-1; row >= 0; row--)
        for (var side = -1; side <= 1; side+=2)
        {
            var id = row+blockOffset;
            var z = .85f+(row+1-progress)*1.9f;
            var width = .68f + Fraction(id*.381966+side*.17)*.6f;
            var height = 1.1f+Fraction(id*.754877+side*.31)*3.8f;
            var inside = side*(1.75f+energy*.28f);
            var outside = inside + side*width;
            var end = z+1.28f;
            var fog = Math.Clamp(1-z/37, .15f, 1);
            var wall = VizColor.Blend(VizColor.FromRgb(5,10,19), Color(scene,.7f), .09f+fog*.12f);
            polygon[0]=View(inside,-height,z); polygon[1]=View(outside,-height,z);
            polygon[2]=View(outside,.32f,z); polygon[3]=View(inside,.32f,z);
            canvas.FillPolygon(polygon,wall);
            polygon[0]=View(inside,-height,z); polygon[1]=View(inside,-height,end);
            polygon[2]=View(inside,.32f,end); polygon[3]=View(inside,.32f,z);
            canvas.FillPolygon(polygon,VizColor.Blend(wall,VizColor.FromRgb(2,5,12),.3f));
            canvas.DrawLine(View(inside,-height,z),View(inside,.32f,z),Color(scene,.4f,(int)(fog*100)),1);
            canvas.DrawLine(View(inside,-height,z),View(inside,-height,end),Color(scene,.65f,(int)(fog*95)),.8f);
            var floors = Detail(scene, 4, 6, 8);
            for(var floor=0; floor<floors; floor++)
            {
                var y = -height+.2f+floor*height/floors;
                var band = SampleRange(scene.SpectrumLevels,(id+floor*3)%32,32);
                var chase = .5f+.5f*MathF.Sin(time*2.1f+floor*.9f-id*.6f);
                var alpha = (int)((50+band*155+scene.TransientEnvelope*chase*90)*fog);
                var windowColor=VizColor.Blend(Color(scene,floor/(float)floors),VizColor.FromRgb(206,227,255),.24f).WithAlpha(alpha);
                canvas.DrawLine(View(inside-side*.015f,y,z+.15f),View(inside-side*.015f,y,end-.15f),windowColor,Math.Clamp(11/z,.85f,2.5f));
                for(var pane=0;pane<3;pane++)
                {
                    var start=z+.13f+pane*.34f;
                    var endPane=start+.19f;
                    var x=inside-side*.018f;
                    polygon[0]=View(x,y-.07f,start);polygon[1]=View(x,y-.07f,endPane);
                    polygon[2]=View(x,y+.02f,endPane);polygon[3]=View(x,y+.02f,start);
                    var lit=Fraction(id*.17+floor*.65+pane*.39)>.32f;
                    canvas.FillPolygon(polygon,windowColor.WithAlpha(lit?alpha:alpha/5));
                }
                if(floor%2==0) canvas.DrawLine(View(inside+side*.14f,y,z-.01f),View(outside-side*.12f,y,z-.01f),Color(scene,.2f,alpha/2),Math.Clamp(6/z,.5f,1.5f));
            }
            // Elevated connections and passing light pulses deepen the scene.
            if(id%4==0)
            {
                var y = -height*.55f;
                canvas.DrawLine(View(-1.75f,y,end),View(1.75f,y,end),Color(scene,.7f,(int)(fog*(40+scene.MidEnvelope*100))),2);
                var x = (Fraction(time*.4+id*.3)*2-1)*1.75f;
                canvas.DrawLine(View(x-.16f,y,end),View(x+.16f,y,end),Color(scene,.1f,(int)(fog*200)),2);
            }
        }
        canvas.Restore();
        Finish(canvas,bounds,scene);
    }
}

public sealed class NeonTerrainRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas,VizRect bounds,VisualizerScene scene)
    {
        if(bounds.Width<2||bounds.Height<2)return;
        Space(canvas,bounds,scene);
        canvas.PushClipRect(bounds);
        var columns=Detail(scene,20,28,36);
        var rows=Detail(scene,24,36,46);
        Span<Vector2> previous=stackalloc Vector2[37];
        Span<Vector2> current=stackalloc Vector2[37];
        Span<Vector2> face=stackalloc Vector2[4];
        var time=(float)scene.MotionTimeSeconds;
        var shift=Fraction(time*20)*.55f;
        var energy=scene.MusicalEnergy;
        var history=scene.TerrainHistory;
        float Height(float u,int age)
        {
            var frequency=u*31;
            var band=(int)frequency;
            float Sample(int bin)
            {
                if(history.Length==0)return SampleRange(scene.SpectrumLevels,bin,32);
                return history[(scene.TerrainNewestIndex-Math.Min(age,history.Length-1)+history.Length)%history.Length][bin];
            }
            var audio=Sample(band)+(Sample(Math.Min(31,band+1))-Sample(band))*(frequency-band);
            // Broad bass swells and smaller frequency ridges, with a shallow
            // central passage. Rows are actual prior spectra, not random hills.
            return audio*(.6f+1.65f*MathF.Abs(u-.5f)) + scene.BassEnvelope*.24f;
        }
        var horizon=bounds.Top+bounds.Height*.36f;
        canvas.FillRadialGlow(new(bounds.CenterX-bounds.Width*.35f,horizon-bounds.Height*.2f,bounds.Width*.7f,bounds.Height*.4f),Color(scene,.7f,35));
        for(var row=rows;row>=0;row--)
        {
            var z=1.1f+row*.55f-shift;
            var age=rows-row;
            for(var col=0;col<=columns;col++)
            {
                var u=col/(float)columns;
                var x=(u-.5f)*13;
                var height=Height(u,age);
                var point=Project(new(x,1.35f-height,z),bounds,.92f);
                point.Y-=bounds.Height*.14f;
                current[col]=point;
                if(row<rows&&col>0)
                {
                    face[0]=previous[col-1];face[1]=previous[col];face[2]=current[col];face[3]=current[col-1];
                    canvas.FillPolygon(face,VizColor.Blend(VizColor.FromRgb(4,5,13),Color(scene,.65f),.035f+height*.04f));
                }
            }
            var fade=Math.Clamp(1-row/(float)(rows+5),.08f,1);
            var color=VizColor.Blend(Color(scene,row/(float)rows),VizColor.FromRgb(184,214,255),.16f).WithAlpha((int)(60+fade*155+energy*30));
            canvas.DrawPolyline(current[..(columns+1)],color,1+fade*.7f);
            if(row<rows)
                for(var col=0;col<=columns;col++)canvas.DrawLine(previous[col],current[col],Color(scene,col/(float)columns,(int)(45+fade*125)),.9f);
            current.CopyTo(previous);
        }
        canvas.Restore();Finish(canvas,bounds,scene);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class SongWarsBracketView : ScrollableControl
{
    private readonly ThemePalette p;
    private readonly System.Windows.Forms.Timer animationTimer;
    private SongWarsTournament? tournament;
    private string? highlightedMatchId;
    private float animationPhase;

    public SongWarsBracketView(ThemePalette palette)
    {
        p = palette;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = p.SurfaceBackColor;
        ForeColor = p.TextPrimaryColor;
        AutoScroll = true;

        animationTimer = new System.Windows.Forms.Timer { Interval = 33 };
        animationTimer.Tick += (_, _) =>
        {
            animationPhase = (animationPhase + 0.035f) % 1f;
            Invalidate();
        };
        animationTimer.Start();
    }

    public void SetTournament(SongWarsTournament? nextTournament, string? nextHighlightedMatchId)
    {
        tournament = nextTournament;
        highlightedMatchId = nextHighlightedMatchId;
        UpdateScrollSize();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            animationTimer.Dispose();

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(p.SurfaceBackColor);

        if (tournament is null || tournament.Matches.Count == 0)
        {
            DrawEmpty(g);
            return;
        }

        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        var layout = BuildLayout(new Rectangle(Point.Empty, AutoScrollMinSize));
        if (layout.Count == 0)
        {
            g.ResetTransform();
            DrawEmpty(g);
            return;
        }

        DrawConnectors(g, layout);
        foreach (var node in layout.Values.OrderBy(n => n.Match.CompletedAtUtc ?? DateTimeOffset.MaxValue))
            DrawMatchNode(g, node);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
    }

    private void UpdateScrollSize()
    {
        var (columns, maxRows) = GetLayoutCounts();
        var width = Math.Max(ClientSize.Width, 40 + columns * 166);
        var height = Math.Max(ClientSize.Height, 80 + maxRows * 82);
        AutoScrollMinSize = new Size(width, height);
    }

    private (int Columns, int MaxRows) GetLayoutCounts()
    {
        if (tournament is null || tournament.Matches.Count == 0)
            return (1, 1);

        var groups = tournament.Matches
            .GroupBy(RoundKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (Math.Max(1, groups.Count), Math.Max(1, groups.Max(g => g.Count())));
    }

    private void DrawEmpty(Graphics g)
    {
        using var font = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var brush = new SolidBrush(p.TextMutedColor);
        TextRenderer.DrawText(
            g,
            "Create or open a tournament to view its bracket.",
            font,
            ClientRectangle,
            p.TextMutedColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private Dictionary<string, BracketNode> BuildLayout(Rectangle bounds)
    {
        var result = new Dictionary<string, BracketNode>(StringComparer.OrdinalIgnoreCase);
        if (tournament is null)
            return result;

        var matches = tournament.Matches
            .OrderBy(m => m.Bracket)
            .ThenBy(m => m.RoundIndex)
            .ThenBy(m => tournament.MatchOrder.IndexOf(m.MatchId))
            .ToList();

        var roundKeys = matches
            .Select(m => RoundKey(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableWidth = Math.Max(320, bounds.Width - 40);
        var availableHeight = Math.Max(220, bounds.Height - 42);
        var columnCount = Math.Max(1, roundKeys.Count);
        var columnWidth = Math.Clamp((availableWidth - 18) / (float)columnCount, 138f, 230f);
        var nodeWidth = Math.Max(122f, Math.Min(180f, columnWidth - 18f));
        var nodeHeight = bounds.Height < 310 ? 58f : 68f;
        var left = bounds.Left + 20f;
        var top = bounds.Top + 32f;

        for (var column = 0; column < roundKeys.Count; column++)
        {
            var key = roundKeys[column];
            var roundMatches = matches.Where(m => string.Equals(RoundKey(m), key, StringComparison.OrdinalIgnoreCase)).ToList();
            var gap = roundMatches.Count <= 1
                ? 0f
                : Math.Max(12f, (availableHeight - (roundMatches.Count * nodeHeight)) / (roundMatches.Count - 1));
            var columnHeight = (roundMatches.Count * nodeHeight) + (Math.Max(0, roundMatches.Count - 1) * gap);
            var y = top + Math.Max(0f, (availableHeight - columnHeight) * 0.5f);
            var x = left + column * columnWidth;

            for (var i = 0; i < roundMatches.Count; i++)
            {
                var rect = new RectangleF(x, y + (i * (nodeHeight + gap)), nodeWidth, nodeHeight);
                result[roundMatches[i].MatchId] = new BracketNode(roundMatches[i], rect, key);
            }
        }

        return result;
    }

    private void DrawConnectors(Graphics g, Dictionary<string, BracketNode> layout)
    {
        if (tournament is null)
            return;

        foreach (var source in layout.Values)
        {
            if (source.Match.Result is SongWarsOutcome.Pending or SongWarsOutcome.Skip || string.IsNullOrWhiteSpace(source.Match.WinnerSubmissionId))
                continue;

            var target = FindNextMatchForWinner(source.Match, layout);
            if (target is null)
                continue;

            var active = string.Equals(target.Match.MatchId, tournament.CurrentMatchId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(source.Match.MatchId, highlightedMatchId, StringComparison.OrdinalIgnoreCase);
            DrawConnector(g, source.Bounds, target.Bounds, active);
        }
    }

    private BracketNode? FindNextMatchForWinner(SongWarsMatch source, Dictionary<string, BracketNode> layout)
    {
        if (tournament is null || string.IsNullOrWhiteSpace(source.WinnerSubmissionId))
            return null;

        var sourceIndex = tournament.MatchOrder.IndexOf(source.MatchId);
        return tournament.MatchOrder
            .Skip(Math.Max(0, sourceIndex + 1))
            .Select(id => layout.TryGetValue(id, out var node) ? node : null)
            .FirstOrDefault(node => node is not null &&
                                    (string.Equals(node.Match.SlotASubmissionId, source.WinnerSubmissionId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(node.Match.SlotBSubmissionId, source.WinnerSubmissionId, StringComparison.OrdinalIgnoreCase)));
    }

    private void DrawConnector(Graphics g, RectangleF from, RectangleF to, bool active)
    {
        var start = new PointF(from.Right, from.Top + from.Height / 2f);
        var end = new PointF(to.Left, to.Top + to.Height / 2f);
        var midX = start.X + Math.Max(18f, (end.X - start.X) * 0.48f);

        using var path = new GraphicsPath();
        path.AddBezier(
            start,
            new PointF(midX, start.Y),
            new PointF(midX, end.Y),
            end);

        var baseColor = active
            ? p.AccentPrimaryColor
            : ThemePalette.Blend(p.BorderColor, p.TextMutedColor, 0.25f);
        using var pen = new Pen(ThemePalette.WithAlpha(baseColor, active ? 215 : 110), active ? 2.4f : 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            DashStyle = active ? DashStyle.Dash : DashStyle.Solid,
            DashOffset = active ? -animationPhase * 18f : 0f
        };
        g.DrawPath(pen, path);

        if (!active)
            return;

        var dot = PointOnCubic(start, new PointF(midX, start.Y), new PointF(midX, end.Y), end, animationPhase);
        using var glow = new SolidBrush(ThemePalette.WithAlpha(p.AccentPrimaryColor, 200));
        g.FillEllipse(glow, dot.X - 3.5f, dot.Y - 3.5f, 7f, 7f);
    }

    private void DrawMatchNode(Graphics g, BracketNode node)
    {
        if (tournament is null)
            return;

        var match = node.Match;
        var isCurrent = string.Equals(match.MatchId, tournament.CurrentMatchId, StringComparison.OrdinalIgnoreCase);
        var isHighlighted = string.Equals(match.MatchId, highlightedMatchId, StringComparison.OrdinalIgnoreCase);
        var completed = match.Result != SongWarsOutcome.Pending;
        var pulse = 0.5f + (float)Math.Sin(animationPhase * MathF.Tau) * 0.5f;
        var rect = node.Bounds;

        using var path = Rounded(rect, 7f);
        var fill = completed
            ? ThemePalette.Blend(p.SurfaceRaisedColor, p.AccentPrimaryColor, p.IsDark ? 0.10f : 0.06f)
            : p.SurfaceAltBackColor;
        if (isCurrent)
            fill = ThemePalette.Blend(fill, p.AccentPrimaryColor, 0.16f + pulse * 0.07f);
        if (isHighlighted)
            fill = ThemePalette.Blend(fill, p.AccentSecondaryColor, 0.18f);

        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        var border = isCurrent || isHighlighted
            ? ThemePalette.Blend(p.AccentPrimaryColor, Color.White, pulse * 0.22f)
            : completed
                ? p.BorderStrongColor
                : p.BorderColor;
        using (var pen = new Pen(border, isCurrent || isHighlighted ? 2f : 1f))
            g.DrawPath(pen, path);

        var titleColor = completed ? p.TextPrimaryColor : p.TextSecondaryColor;
        using var roundFont = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 7.5f);

        var subA = FindSubmission(match.SlotASubmissionId)?.DisplayTitle ?? "Track A";
        var subB = FindSubmission(match.SlotBSubmissionId)?.DisplayTitle ?? "Track B";
        var winner = match.WinnerSubmissionId is { } winnerId
            ? FindSubmission(winnerId)?.DisplayTitle
            : null;

        TextRenderer.DrawText(g, FriendlyRound(match), roundFont, RoundRect(rect), p.TextMutedColor, NodeTextFlags);
        TextRenderer.DrawText(g, "A  " + subA, titleFont, LineRect(rect, 18), titleColor, NodeTextFlags);
        TextRenderer.DrawText(g, "B  " + subB, titleFont, LineRect(rect, 35), titleColor, NodeTextFlags);

        var status = isCurrent
            ? "LIVE - " + FriendlyPhase(match.Phase)
            : completed
                ? ResultText(match, winner)
                : FriendlyPhase(match.Phase);
        var statusColor = isCurrent ? p.AccentPrimaryColor : completed ? p.TextSecondaryColor : p.TextMutedColor;
        TextRenderer.DrawText(g, status, metaFont, LineRect(rect, rect.Height > 62 ? 52 : 50), statusColor, NodeTextFlags);
    }

    private SongWarsSubmission? FindSubmission(string? submissionId) =>
        string.IsNullOrWhiteSpace(submissionId) || tournament is null
            ? null
            : tournament.Submissions.FirstOrDefault(s =>
                string.Equals(s.SubmissionId, submissionId, StringComparison.OrdinalIgnoreCase));

    private static string RoundKey(SongWarsMatch match) => $"{match.Bracket}:{match.RoundIndex}:{match.RoundId}";

    private static string FriendlyRound(SongWarsMatch match) =>
        match.Bracket switch
        {
            SongWarsBracket.Winners => $"Winners R{match.RoundIndex}",
            SongWarsBracket.Losers => $"Losers R{match.RoundIndex}",
            SongWarsBracket.GrandFinals => match.RoundIndex > 1 ? "Grand Reset" : "Grand Finals",
            _ => match.RoundId
        };

    private static string FriendlyPhase(SongWarsMatchPhase phase) =>
        phase switch
        {
            SongWarsMatchPhase.Pending => "Queued",
            SongWarsMatchPhase.Ready => "Ready",
            SongWarsMatchPhase.TrackAPlaying => "Playing A",
            SongWarsMatchPhase.TrackBPlaying => "Playing B",
            SongWarsMatchPhase.PrimaryVoting => "Voting",
            SongWarsMatchPhase.EliminationVoting => "Elim vote",
            SongWarsMatchPhase.Complete => "Complete",
            SongWarsMatchPhase.Skipped => "Requeued",
            SongWarsMatchPhase.Paused => "Paused",
            _ => phase.ToString()
        };

    private static string ResultText(SongWarsMatch match, string? winner)
    {
        if (match.Result == SongWarsOutcome.Skip)
            return "Skipped";

        if (match.Result == SongWarsOutcome.Eliminated)
        {
            var snapshot = match.VoteSnapshots.LastOrDefault();
            return snapshot?.EliminatedSlot == SongWarsMatchSlot.A
                ? "A eliminated"
                : snapshot?.EliminatedSlot == SongWarsMatchSlot.B
                    ? "B eliminated"
                    : "Eliminated";
        }

        return string.IsNullOrWhiteSpace(winner) ? match.Result.ToString() : $"Winner: {winner}";
    }

    private static Rectangle RoundRect(RectangleF rect) =>
        Rectangle.Round(new RectangleF(rect.X + 10, rect.Y + 6, rect.Width - 20, 14));

    private static Rectangle LineRect(RectangleF rect, float yOffset) =>
        Rectangle.Round(new RectangleF(rect.X + 10, rect.Y + yOffset, rect.Width - 20, 16));

    private static TextFormatFlags NodeTextFlags =>
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static PointF PointOnCubic(PointF a, PointF b, PointF c, PointF d, float t)
    {
        var u = 1f - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        return new PointF(
            uuu * a.X + 3f * uu * t * b.X + 3f * u * tt * c.X + ttt * d.X,
            uuu * a.Y + 3f * uu * t * b.Y + 3f * u * tt * c.Y + ttt * d.Y);
    }

    private sealed record BracketNode(SongWarsMatch Match, RectangleF Bounds, string RoundKey);
}

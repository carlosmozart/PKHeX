using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Barra de progresso da atualizacao: uma fileira de Pokebolas cinzas que ganham cor (cada uma de um tipo) conforme o
/// download avanca, com a equipe do save aberto andando em fila sobre a barra. Os sprites sao parados: o "andar" e um
/// pulinho de poucos pixels a cada passo, como os Pokemon que seguem o treinador no HeartGold/SoulSilver.
/// </summary>
public sealed class UpdateAnimation : Avalonia.Controls.Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<UpdateAnimation, double>(nameof(Progress));

    /// <summary>0-100.</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>Quem anda sobre a barra (a equipe do save aberto); vazio = um inicial aleatorio.</summary>
    public static Func<IReadOnlyList<(ushort Species, bool Shiny)>>? Walkers { get; set; }

    /// <summary>Ordem das bolas: as classicas primeiro, a Master Ball fecha os 100%.</summary>
    private static readonly byte[] BallOrder = [4, 3, 2, 12, 7, 6, 11, 13, 15, 21, 23, 25, 26, 1];
    private static readonly ushort[] Starters = [25, 1, 4, 7, 133, 152, 155, 158, 252, 255, 258, 387, 390, 393, 495, 498, 501, 650, 653, 656, 722, 725, 728, 810, 813, 816, 906, 909, 912];

    private const double BallSize = 30, WalkerSize = 56, Gap = 3;
    private readonly DispatcherTimer _timer;
    private readonly List<Bitmap> _walkers = [];
    private readonly double[] _pop = new double[BallOrder.Length];
    private int _lit;
    private double _walkX = -1;
    private long _tick;

    public UpdateAnimation()
    {
        Height = WalkerSize + BallSize + 14;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => Step());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LoadWalkers();
        _lit = Lit(Progress);
        _walkX = -1;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ProgressProperty)
            return;
        int lit = Lit(Progress);
        if (lit < _lit) // recomecou (ex.: demonstracao de novo)
        {
            Array.Clear(_pop);
            _walkX = -1;
            LoadWalkers();
        }
        for (int i = _lit; i < lit && i < _pop.Length; i++)
            _pop[i] = 1; // acabou de ganhar cor: "pop" de captura
        _lit = lit;
    }

    private static int Lit(double progress) => (int)Math.Floor(Math.Clamp(progress, 0, 100) / 100 * BallOrder.Length + 1e-9);

    private void LoadWalkers()
    {
        _walkers.Clear();
        var rng = Random.Shared;
        IReadOnlyList<(ushort Species, bool Shiny)> list;
        try { list = Walkers?.Invoke() ?? []; }
        catch { list = []; }
        foreach (var (species, shiny) in list)
        {
            // Shiny raro: de vez em quando um Pokemon da fila aparece brilhante.
            if (species != 0 && SpriteService.GetSpeciesSprite(species, shiny || rng.Next(20) == 0) is { } bmp)
                _walkers.Add(bmp);
            if (_walkers.Count == 6)
                break;
        }
        if (_walkers.Count == 0 && SpriteService.GetSpeciesSprite(Starters[rng.Next(Starters.Length)], rng.Next(20) == 0) is { } starter)
            _walkers.Add(starter);
    }

    private void Step()
    {
        _tick++;
        for (int i = 0; i < _pop.Length; i++)
            _pop[i] = Math.Max(0, _pop[i] - 0.12);
        // A fila segue a ponta do progresso sem teleportar (anda ate la).
        double target = TrackLeft + TrackWidth * Math.Clamp(Progress, 0, 100) / 100;
        _walkX = _walkX < 0 ? TrackLeft : _walkX + Math.Clamp(target - _walkX, -6, 6);
        InvalidateVisual();
    }

    private static double TrackLeft => WalkerSize / 2;
    private double TrackWidth => Math.Max(0, Bounds.Width - WalkerSize);

    public override void Render(DrawingContext context)
    {
        int n = BallOrder.Length;
        double step = TrackWidth / n;
        double size = Math.Min(BallSize, step - Gap);
        if (size <= 0)
            return;
        double ballsTop = WalkerSize + 8;
        bool done = _lit >= n;

        using var pixels = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        for (int i = 0; i < n; i++)
        {
            var bmp = i < _lit ? SpriteService.GetBallSprite(BallOrder[i]) : SpriteService.GetGrayBallSprite(BallOrder[i]);
            if (bmp is null)
                continue;
            double s = size * (1 + 0.35 * Math.Sin(_pop[i] * Math.PI));
            if (done) // tudo pronto: as bolas brilham em sequencia
                s *= 1 + 0.15 * Math.Max(0, Math.Sin(_tick * 0.25 - i * 0.6));
            double cx = TrackLeft + step * (i + 0.5);
            double cy = ballsTop + size / 2;
            context.DrawImage(bmp, new Rect(cx - s / 2, cy - s / 2, s, s));
        }

        if (_walkX < 0)
            return;
        double spacing = WalkerSize * 0.85;
        for (int k = 0; k < _walkers.Count; k++)
        {
            double x = _walkX - k * spacing;
            if (x < TrackLeft - 1)
                break; // ainda nao entrou na barra
            var bmp = _walkers[k];
            // Pulinho a cada passo (defasado na fila); parado, so respira; no fim, pulos de comemoracao.
            bool walking = Math.Abs(TrackLeft + TrackWidth * Math.Clamp(Progress, 0, 100) / 100 - _walkX) > 0.5;
            double hop = done ? Math.Abs(Math.Sin(_tick * 0.2 - k * 0.6)) * 10
                : walking ? Math.Abs(Math.Sin(_tick * 0.55 - k * 1.3)) * 3
                : Math.Abs(Math.Sin(_tick * 0.12 - k * 0.8)) * 1.5;
            double scale = Math.Min(WalkerSize / bmp.Size.Width, WalkerSize / bmp.Size.Height);
            double bw = bmp.Size.Width * scale, bh = bmp.Size.Height * scale;
            // Os sprites olham para a esquerda: espelhados, a fila anda de frente para a direita.
            using (context.PushTransform(Matrix.CreateTranslation(-x, 0) * Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(x, 0)))
                context.DrawImage(bmp, new Rect(x - bw / 2, WalkerSize - bh - hop + 4, bw, bh));
        }
    }
}

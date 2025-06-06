﻿#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface IRendererFactory
{
    RenderQuality GlobalQuality { get; set; }

    ISpectrumRenderer CreateRenderer(
        RenderStyle style,
        bool isOverlayActive,
        RenderQuality? quality = null,
        CancellationToken cancellationToken = default);

    IEnumerable<ISpectrumRenderer> GetAllRenderers();

    void ConfigureAllRenderers(
        bool? isOverlayActive,
        RenderQuality? quality = null);
}
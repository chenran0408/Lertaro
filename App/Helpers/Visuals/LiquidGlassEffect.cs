using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
namespace Lertaro.App.Helpers.Visuals;

internal sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly PixelShader Shader = Load();
    private static PixelShader Load()
    {
        _ = System.Windows.Application.ResourceAssembly;
        var shader = new PixelShader { UriSource = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Shaders/LiquidGlass.ps") };
        shader.Freeze(); return shader;
    }
    internal static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(LiquidGlassEffect), 0);
    internal static readonly DependencyProperty BoundsProperty = DependencyProperty.Register("Bounds", typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(0)));
    internal static readonly DependencyProperty ShapeProperty = DependencyProperty.Register("Shape", typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(1)));
    internal static readonly DependencyProperty OpticsProperty = DependencyProperty.Register("Optics", typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(2)));
    internal LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty); UpdateShaderValue(BoundsProperty); UpdateShaderValue(ShapeProperty); UpdateShaderValue(OpticsProperty);
    }
    internal System.Windows.Media.Brush Input { get => (System.Windows.Media.Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    internal Point4D Bounds { get => (Point4D)GetValue(BoundsProperty); set => SetValue(BoundsProperty, value); }
    internal Point4D Shape { get => (Point4D)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    internal Point4D Optics { get => (Point4D)GetValue(OpticsProperty); set => SetValue(OpticsProperty, value); }
}

using System.Linq;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class SetterSpecCollectorTests
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void StyleTargetType_Context_IsCollected()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}">
          <Style TargetType="Button">
            <Setter Property="Margin"/>
          </Style>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.Equal("Button", spec.TypeName);
        Assert.Equal("", spec.TypePrefix);
        Assert.Equal("Margin", spec.PropertyName);
        Assert.False(spec.IsAttached);
    }

    [Fact]
    public void TriggerSetters_InheritStyleTargetType()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}">
          <Style TargetType="Button">
            <Style.Triggers>
              <Trigger Property="IsPressed">
                <Setter Property="Padding"/>
              </Trigger>
            </Style.Triggers>
          </Style>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.Equal("Button", spec.TypeName);
        Assert.Equal("Padding", spec.PropertyName);
    }

    [Fact]
    public void ControlTemplateTriggers_UseTemplateTargetType()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}">
          <ControlTemplate TargetType="ComboBox">
            <ControlTemplate.Triggers>
              <Trigger>
                <Setter Property="Foreground"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.Equal("ComboBox", spec.TypeName);
    }

    [Fact]
    public void XTypeMarkupExtension_IsStripped()
    {
        var targetType = "{x:Type ToggleButton}";
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}" xmlns:x="{X}">
          <Style TargetType="{targetType}">
            <Setter Property="Padding"/>
          </Style>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.Equal("ToggleButton", spec.TypeName);
    }

    [Fact]
    public void PrefixedTargetType_CarriesPrefix()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}" xmlns:local="clr-namespace:App.Controls">
          <Style TargetType="local:Pager">
            <Setter Property="PageSize"/>
          </Style>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.Equal("Pager", spec.TypeName);
        Assert.Equal("local", spec.TypePrefix);
    }

    [Fact]
    public void AttachedPropertyForm_CarriesOwnerPrefix()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}">
          <Style TargetType="Button">
            <Setter Property="TextBlock.FontSize"/>
          </Style>
        </ResourceDictionary>
        """);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        Assert.True(spec.IsAttached);
        Assert.Equal("TextBlock", spec.PropertyPrefix);
        Assert.Equal("FontSize", spec.PropertyName);
    }

    [Fact]
    public void ContextlessSetter_IsSkipped()
    {
        var index = XamlIndex.Parse($"""
        <ResourceDictionary xmlns="{P}">
          <Setter Property="Margin"/>
        </ResourceDictionary>
        """);
        Assert.Empty(SetterSpecCollector.Collect(index));
    }

    [Fact]
    public void ValueSpan_PointsAtPropertyAttribute()
    {
        var xaml = $"""
        <ResourceDictionary xmlns="{P}">
          <Style TargetType="Button">
            <Setter Property="MarginX"/>
          </Style>
        </ResourceDictionary>
        """;
        var index = XamlIndex.Parse(xaml);
        var spec = Assert.Single(SetterSpecCollector.Collect(index));
        var offset = index.OffsetOf(spec.Line, spec.Column);
        Assert.Equal("MarginX", xaml.Substring(offset, spec.Length));
    }
}

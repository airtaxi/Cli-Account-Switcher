using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace CodexAccountSwitch.WinUI.Services;

public sealed class ApplicationThemeService(ElementTheme initialTheme)
{
    private readonly List<WeakReference<FrameworkElement>> _themeTargetReferences = [];

    public event Action<ElementTheme> ThemeChanged;

    public ElementTheme CurrentTheme { get; private set; } = initialTheme;

    public void ApplyTheme(ElementTheme theme)
    {
        if (CurrentTheme == theme) return;

        CurrentTheme = theme;
        ApplyThemeToRegisteredTargets();
        ThemeChanged?.Invoke(theme);
    }

    public void ApplyThemeToElement(FrameworkElement frameworkElement) => frameworkElement.RequestedTheme = CurrentTheme;

    public void ApplyThemeToWindow(Window window)
    {
        if (window.Content is FrameworkElement frameworkElement) ApplyThemeToElement(frameworkElement);
    }

    public void RegisterThemeTarget(FrameworkElement frameworkElement)
    {
        ApplyThemeToElement(frameworkElement);
        for (var themeTargetReferenceIndex = _themeTargetReferences.Count - 1; themeTargetReferenceIndex >= 0; themeTargetReferenceIndex--)
        {
            if (!_themeTargetReferences[themeTargetReferenceIndex].TryGetTarget(out var registeredFrameworkElement))
            {
                _themeTargetReferences.RemoveAt(themeTargetReferenceIndex);
                continue;
            }

            if (ReferenceEquals(registeredFrameworkElement, frameworkElement)) return;
        }

        _themeTargetReferences.Add(new WeakReference<FrameworkElement>(frameworkElement));
    }

    private void ApplyThemeToRegisteredTargets()
    {
        for (var themeTargetReferenceIndex = _themeTargetReferences.Count - 1; themeTargetReferenceIndex >= 0; themeTargetReferenceIndex--)
        {
            if (!_themeTargetReferences[themeTargetReferenceIndex].TryGetTarget(out var frameworkElement))
            {
                _themeTargetReferences.RemoveAt(themeTargetReferenceIndex);
                continue;
            }

            ApplyThemeToElement(frameworkElement);
        }
    }
}

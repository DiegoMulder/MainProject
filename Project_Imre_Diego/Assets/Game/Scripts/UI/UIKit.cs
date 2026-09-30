using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace SurvivalFP
{
    // Builders for the shared visual language defined in UI/Theme/GameUI.uss.
    // New menus compose these instead of styling individual elements.
    public static class UIKit
    {
        public static T With<T>(this T element, params string[] classes) where T : VisualElement
        {
            if (classes != null) foreach (var c in classes) if (!string.IsNullOrEmpty(c)) element.AddToClassList(c);
            return element;
        }
        public static T AddTo<T>(this T element, VisualElement parent) where T : VisualElement { parent.Add(element); return element; }
        public static VisualElement NewBox(params string[] classes) => new VisualElement().With(classes);
        public static Label NewLabel(string text, params string[] classes) => new Label(text).With(classes);
        // Variants: "btn--primary" (main action), "btn--danger" (leaving/destructive), "btn--ghost" (secondary).
        public static Button NewButton(string text, Action onClick, string variant = null)
        {
            var button = new Button(onClick) { text = text }.With("btn", variant);
            button.focusable = true;
            return button;
        }
        public static VisualElement NewScreen(VisualElement root, string name, params string[] classes)
        {
            var screen = NewBox("screen", "screen--hidden").With(classes);
            screen.name = name; root.Add(screen); return screen;
        }
        public static VisualElement NewPanel(VisualElement parent, string heading = null, string size = null)
        {
            var panel = NewBox("panel", size).AddTo(parent);
            if (heading != null) NewLabel(heading, "heading", "panel__header").AddTo(panel);
            return panel;
        }
        public static VisualElement NewDivider(VisualElement parent) => NewBox("divider").AddTo(parent);
        // Navigation is words, never boxes: on hover or focus a thin crimson line draws itself under the word
        // and a small mark slides in beside it.
        // The word, its line and its mark share a wrapper that shrinks to the word (a Button with children stops
        // measuring its text). Mouse hover and keyboard/gamepad focus light it the same way.
        public static Button NewMenuItem(VisualElement parent, string text, Action click, params string[] variants)
        {
            var wrap = NewBox("menu-item-wrap").AddTo(parent);
            var item = NewButton(text, click, "menu-item").With(variants).AddTo(wrap);
            var line = NewBox("menu-item__line"); line.pickingMode = PickingMode.Ignore; wrap.Add(line);
            var mark = NewLabel("◂", "menu-item__mark"); mark.pickingMode = PickingMode.Ignore; wrap.Add(mark);
            bool pointer = false, focus = false;
            void Refresh() => wrap.EnableInClassList("menu-item-wrap--hot", (pointer || focus) && item.enabledInHierarchy);
            item.RegisterCallback<PointerEnterEvent>(_ => { pointer = true; Refresh(); });
            item.RegisterCallback<PointerLeaveEvent>(_ => { pointer = false; Refresh(); });
            item.RegisterCallback<FocusInEvent>(_ => { focus = true; Refresh(); });
            item.RegisterCallback<FocusOutEvent>(_ => { focus = false; Refresh(); });
            return item;
        }
        // Marks a menu item as the current one (e.g. the open options section): its line and mark stay drawn.
        public static void SetCurrent(this Button item, bool current) => item.parent?.EnableInClassList("menu-item-wrap--current", current);
        // A small underlined action in the functional face (copy code, retry voice).
        public static Button NewLink(VisualElement parent, string text, Action click) => NewButton(text, click, "link").AddTo(parent);
        public static TextField NewField(VisualElement parent, string caption, string value, int maxLength, string variant = null)
        {
            if (caption != null) NewLabel(caption, "label-caps").AddTo(parent);
            var field = new TextField { value = value, maxLength = maxLength }.With("field", variant);
            parent.Add(field); return field;
        }
        // A titled group of settings: title in the heading face, a line of explanation, a thin rule.
        public static VisualElement NewSection(VisualElement parent, string title, string description = null)
        {
            var section = NewBox("section").AddTo(parent);
            var head = NewBox("section__head").AddTo(section);
            NewLabel(title, "section__title").AddTo(head);
            NewBox("section__rule").AddTo(head);
            if (!string.IsNullOrEmpty(description)) NewLabel(description, "section__desc").AddTo(section);
            return section;
        }
        // One setting per row: caption (and optional hint) on the left, the control on the right.
        static VisualElement SettingRow(VisualElement parent, string caption, string hint, out VisualElement control)
        {
            var row = NewBox("setting-row").AddTo(parent);
            var text = NewBox("setting-row__text").AddTo(row);
            NewLabel(caption, "setting-row__caption").AddTo(text);
            if (!string.IsNullOrEmpty(hint)) NewLabel(hint, "setting-row__hint").AddTo(text);
            control = NewBox("setting-row__control").AddTo(row);
            return row;
        }
        // Slider with a filled track and its current value; the fill follows the handle.
        public static Slider NewSlider(VisualElement parent, string caption, float min, float max, Func<float, string> format, Action<float> changed, string hint = null)
        {
            SettingRow(parent, caption, hint, out var control);
            var slider = new Slider(min, max).With("slider").AddTo(control);
            var value = NewLabel("", "setting__value").AddTo(control);
            var fill = NewBox("slider__fill");
            fill.pickingMode = PickingMode.Ignore;
            slider.Q(className: "unity-base-slider__tracker")?.Add(fill);
            void Refresh(float v) { value.text = format(v); fill.style.width = Length.Percent(Mathf.InverseLerp(min, max, v) * 100f); }
            slider.RegisterValueChangedCallback(e => { Refresh(e.newValue); changed(e.newValue); });
            slider.userData = (Action)(() => Refresh(slider.value));
            return slider;
        }
        // Updates the value label and fill without firing change callbacks.
        public static void SetQuiet(this Slider slider, float value)
        {
            slider.SetValueWithoutNotify(value);
            (slider.userData as Action)?.Invoke();
        }
        // A switch written as words, "ON  OFF": the chosen word is lit and underlined, the other sinks back.
        public static Toggle NewToggle(VisualElement parent, string caption, Action<bool> changed, string hint = null)
        {
            SettingRow(parent, caption, hint, out var control);
            var toggle = new Toggle().With("toggle", "switch").AddTo(control);
            var input = toggle.Q(className: "unity-toggle__input");
            var on = NewLabel("ON", "switch__word", "switch__word--on"); on.pickingMode = PickingMode.Ignore;
            var off = NewLabel("OFF", "switch__word", "switch__word--off"); off.pickingMode = PickingMode.Ignore;
            input?.Add(on); input?.Add(off);
            void Refresh(bool value) { toggle.EnableInClassList("switch--on", value); }
            toggle.RegisterValueChangedCallback(e => { Refresh(e.newValue); changed(e.newValue); });
            toggle.userData = (Action)(() => Refresh(toggle.value));
            return toggle;
        }
        public static void SetQuiet(this Toggle toggle, bool value)
        {
            toggle.SetValueWithoutNotify(value);
            (toggle.userData as Action)?.Invoke();
        }
        public static void Show(VisualElement screen, bool visible)
        {
            bool hidden = screen.ClassListContains("screen--hidden");
            if (visible == !hidden) return;
            screen.EnableInClassList("screen--hidden", !visible);
            if (!visible) return;
            screen.AddToClassList("screen--enter");
            screen.schedule.Execute(() => screen.RemoveFromClassList("screen--enter")).StartingIn(16);
        }
        // Avoids relayout/redraw when the value did not change.
        public static void SetText(this Label label, string value) { if (label.text != value) label.text = value; }
    }
}

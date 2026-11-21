using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        void BuildControlSettings(RectTransform body, bool field)
        {
            var input = session.Input;
            Label(body, "操控设置", 28, 24, 12, 450, 35);
            Label(body, "拖动视角和转动手机可分别调整；改动立即生效。", 17, 25, 54, 1100, 28, color: Muted);
            var lookText = Label(body, "", 22, 485, 102, 75, 31, TextAnchor.UpperRight, Amber);
            var gyroText = Label(body, "", 22, 1070, 102, 76, 31, TextAnchor.UpperRight, Amber);
            lookText.text = Percent(input.LookSensitivity);
            gyroText.text = Percent(input.GyroSensitivity);
            SettingsSlider(body, "视角转动灵敏度", 24, 99, 536, 25, 300, input.LookSensitivity * 100f, value =>
            {
                session.SelectControlOptions(value * 0.01f, input.GyroSensitivity, input.GyroUsage, input.AimBehavior);
                lookText.text = Percent(input.LookSensitivity);
            });
            var gyroSlider = SettingsSlider(body, "陀螺仪灵敏度", 610, 99, 536, 25, 300, input.GyroSensitivity * 100f, value =>
            {
                session.SelectControlOptions(input.LookSensitivity, value * 0.01f, input.GyroUsage, input.AimBehavior);
                gyroText.text = Percent(input.GyroSensitivity);
            });
            gyroSlider.interactable = input.HasGyroscope;
            var gyro = new[] { "关闭", "始终使用", "仅开镜" };
            var aim = new[] { "长按开镜", "点击开镜", "混合开镜" };
            Label(body, "陀螺仪", 21, 24, 189, 158, 38);
            Label(body, "开镜方式", 21, 610, 189, 170, 38);
            MakeOptions(gyro, 24, (int)input.GyroUsage, value =>
                session.SelectControlOptions(input.LookSensitivity, input.GyroSensitivity, (GyroMode)value, input.AimBehavior), input.HasGyroscope);
            MakeOptions(aim, 610, (int)input.AimBehavior, value =>
                session.SelectControlOptions(input.LookSensitivity, input.GyroSensitivity, input.GyroUsage, (AimMode)value), true);
            Label(body, input.HasGyroscope ? "仅在局内操作时读取传感器。" : "未检测到陀螺仪，仍可正常拖屏。", 16, 24, 267, 539, 26, color: Muted);
            Label(body, "混合：短按切换；长按松手后回到按下前的状态。", 16, 610, 267, 539, 26, color: Muted);
            Fill(At("Touch divider", body, 24, 302, 1122, 1), Border);
            var threshold = Label(body, "前推 " + input.SprintThreshold + "%", 20, 658, 313, 177, 29, TextAnchor.UpperRight, Amber);
            SettingsSlider(body, "摇杆冲刺阈值", 24, 309, 811, PlayerInputReader.MinSprintThreshold, PlayerInputReader.MaxSprintThreshold, input.SprintThreshold, value =>
            {
                session.SelectSprintThreshold(Mathf.RoundToInt(value));
                threshold.text = "前推 " + input.SprintThreshold + "%";
            });
            Label(body, "百分比越低，越容易触发自动冲刺。", 16, 24, 389, 800, 27, color: Muted);
            Button(body, "自定义键位", 866, 339, 279, 56, () => ShowTouchLayout(field), true);

            void MakeOptions(string[] labels, float x, int selected, Action<int> select, bool enabled)
            {
                var buttons = new Button[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    buttons[i] = Button(body, labels[i], x + 183 * i, 221, 170, 41, () =>
                    {
                        select(index);
                        for (int j = 0; j < buttons.Length; j++) StyleButton(buttons[j], j == index, null);
                    }, i == selected);
                    buttons[i].interactable = enabled;
                }
            }
        }

        static string Percent(float multiplier) => Mathf.RoundToInt(multiplier * 100f) + "%";

        Slider SettingsSlider(Transform owner, string caption, float x, float y, float width, int min, int max, float initial, Action<float> change)
        {
            Label(owner, caption, 20, x, y, width - 76, 31);
            var root = At(caption, owner, x, y + 31, width, 46);
            Fill(root, Color.clear, true);
            var track = At("Track", root, 13, 19, width - 26, 8);
            Fill(track, Border);
            var active = Stretch("Fill", track);
            Fill(active, Aqua);
            var handleSpace = At("Handle bounds", root, 13, 0, width - 26, 46);
            var handle = Anchor("Handle", handleSpace, 0, 0.5f);
            handle.sizeDelta = new Vector2(23, 36);
            var handleImage = Fill(handle, Pale);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = active;
            slider.handleRect = handle;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(initial);
            slider.onValueChanged.AddListener(value => change(value));
            return slider;
        }
    }
}

using System;
using System.IO;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class CaptionVocabularyChecks
{
    internal static void Run(Action<bool, string> check, string temporaryRoot)
    {
        TestBuiltInCorrections(check);
        TestCustomKeywords(check);
        TestJargonPreservationInTranslation(check);
        TestCaptionSettingsPersistence(check, temporaryRoot);
        TestLocalizationKeys(check);
    }

    private static void TestBuiltInCorrections(Action<bool, string> check)
    {
        var service = new CaptionVocabularyService();

        // 1. Antigravity acoustic variations
        check(service.CorrectText("I am developing with anti gravity today") == "I am developing with Antigravity today",
            "Vocabulary corrects 'anti gravity' to 'Antigravity'");
        check(service.CorrectText("Check anti-gravity agent logs") == "Check Antigravity agent logs",
            "Vocabulary corrects 'anti-gravity' to 'Antigravity'");

        // 2. Gemini acoustic confusion
        check(service.CorrectText("The jiminy model responded quickly") == "The Gemini model responded quickly",
            "Vocabulary corrects 'jiminy' to 'Gemini'");

        // 3. Driver Booster & StreamGuard branding
        check(service.CorrectText("Launch driver booster now") == "Launch Driver Booster now",
            "Vocabulary corrects 'driver booster' to 'Driver Booster'");
        check(service.CorrectText("stream guard is protecting your screen") == "StreamGuard is protecting your screen",
            "Vocabulary corrects 'stream guard' to 'StreamGuard'");

        // 4. Programming languages and frameworks
        check(service.CorrectText("We wrote this in c-sharp and dot net") == "We wrote this in C# and .NET",
            "Vocabulary corrects 'c-sharp' and 'dot net' to 'C#' and '.NET'");
        check(service.CorrectText("Running on coobernetes cluster") == "Running on Kubernetes cluster",
            "Vocabulary corrects 'coobernetes' to 'Kubernetes'");
        check(service.CorrectText("Compiled with web assembly") == "Compiled with WebAssembly",
            "Vocabulary corrects 'web assembly' to 'WebAssembly'");

        // 5. Empty / whitespace safety
        check(service.CorrectText("") == "", "Vocabulary handles empty string gracefully");
        check(service.CorrectText("   ") == "   ", "Vocabulary handles whitespace gracefully");
    }

    private static void TestCustomKeywords(Action<bool, string> check)
    {
        var service = new CaptionVocabularyService("TensorRT, ONNX, WebRTC, WebSocket");

        check(service.CorrectText("optimizing with tensorrt engine") == "optimizing with TensorRT engine",
            "Custom keywords correct 'tensorrt' to 'TensorRT'");
        check(service.CorrectText("exporting model to onnx format") == "exporting model to ONNX format",
            "Custom keywords correct 'onnx' to 'ONNX'");
        check(service.CorrectText("connecting via webrtc and websocket") == "connecting via WebRTC and WebSocket",
            "Custom keywords correct multiple custom terms in single sentence");

        // Dynamic update
        service.UpdateKeywords("FastAPI, Redis");
        check(service.CorrectText("serving via fastapi and redis cache") == "serving via FastAPI and Redis cache",
            "Dynamic UpdateKeywords refreshes active rules correctly");

        // Quick clear keywords
        service.UpdateKeywords(string.Empty);
        check(service.CorrectText("serving via fastapi and redis cache") == "serving via fastapi and redis cache",
            "Clearing keywords removes custom rules so 'fastapi' is untouched");
        check(service.CorrectText("protecting with driver booster") == "protecting with Driver Booster",
            "Clearing custom keywords preserves built-in dictionary rules");
    }

    private static void TestJargonPreservationInTranslation(Action<bool, string> check)
    {
        var service = new CaptionVocabularyService();

        // 1. Driver Booster preservation
        var orig1 = "I am using Driver Booster to protect windows";
        var trans1 = "Tôi đang sử dụng bộ tăng áp driver để bảo vệ cửa sổ";
        var res1 = service.PreserveJargonInTranslation(orig1, trans1);
        check(res1 == "Tôi đang sử dụng Driver Booster để bảo vệ cửa sổ",
            "PreserveJargonInTranslation restores 'Driver Booster' from literal Vietnamese translation");

        // 2. Antigravity preservation
        var orig2 = "Running Antigravity workflow";
        var trans2 = "Đang chạy quy trình chống trọng lực";
        var res2 = service.PreserveJargonInTranslation(orig2, trans2);
        check(res2 == "Đang chạy quy trình Antigravity",
            "PreserveJargonInTranslation restores 'Antigravity' from literal 'chống trọng lực'");

        // 3. StreamGuard preservation
        var orig3 = "StreamGuard is active";
        var trans3 = "bảo vệ luồng đang hoạt động";
        var res3 = service.PreserveJargonInTranslation(orig3, trans3);
        check(res3 == "StreamGuard đang hoạt động",
            "PreserveJargonInTranslation restores 'StreamGuard' from literal 'bảo vệ luồng'");

        // 4. Original without keyword leaves translation untouched
        var orig4 = "Good morning everyone";
        var trans4 = "Chào buổi sáng mọi người";
        var res4 = service.PreserveJargonInTranslation(orig4, trans4);
        check(res4 == trans4, "PreserveJargonInTranslation leaves unaffected sentences untouched");
    }

    private static void TestCaptionSettingsPersistence(Action<bool, string> check, string temporaryRoot)
    {
        var testPath = Path.Combine(temporaryRoot, $"caption_settings_vocab_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new CaptionSettings
            {
                CustomKeywords = "TensorFlow, PyTorch, C#, .NET, WebRTC",
                EnableTranslation = true,
                TranslationMode = "Bilingual"
            };
            settings.Save(testPath);
            check(File.Exists(testPath), "CaptionSettings file created with custom keywords");

            var loaded = CaptionSettings.Load(testPath);
            check(loaded.CustomKeywords == "TensorFlow, PyTorch, C#, .NET, WebRTC",
                "CaptionSettings loaded CustomKeywords accurately");
            check(loaded.EnableTranslation == true, "CaptionSettings EnableTranslation persisted");
            check(loaded.TranslationMode == "Bilingual", "CaptionSettings TranslationMode persisted");

            // Empty CustomKeywords persistence (quick clear)
            var emptySettings = new CaptionSettings
            {
                CustomKeywords = string.Empty
            };
            emptySettings.Save(testPath);
            var loadedEmpty = CaptionSettings.Load(testPath);
            check(loadedEmpty.CustomKeywords == string.Empty, "CaptionSettings accurately persists empty CustomKeywords when cleared");

            // Normalization check
            var unnormalized = new CaptionSettings { CustomKeywords = null! };
            unnormalized.Normalize();
            check(!string.IsNullOrEmpty(unnormalized.CustomKeywords) && unnormalized.CustomKeywords.Contains("Antigravity"), 
                "CaptionSettings.Normalize restores sensible default keywords when null");
        }
        finally
        {
            if (File.Exists(testPath))
                File.Delete(testPath);
        }
    }

    private static void TestLocalizationKeys(Action<bool, string> check)
    {
        var keys = new[]
        {
            "Captions.VocabularySection",
            "Captions.VocabularyHint",
            "Captions.SaveVocabulary",
            "Captions.ClearVocabulary",
            "Captions.ClearVocabularyTooltip",
            "Captions.KeywordsSavedNotice",
            "Captions.KeywordsClearedNotice"
        };

        foreach (var key in keys)
        {
            var vi = LocalizationService.GetString(key, "vi-VN");
            var en = LocalizationService.GetString(key, "en-US");

            check(!string.IsNullOrWhiteSpace(vi) && vi != key, $"Localization key '{key}' has Vietnamese text: {vi}");
            check(!string.IsNullOrWhiteSpace(en) && en != key, $"Localization key '{key}' has English text: {en}");
        }
    }
}

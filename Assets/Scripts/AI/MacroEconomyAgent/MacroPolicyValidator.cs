using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcTrading.MacroAgent
{
    public static class MacroPolicyValidator
    {
        public const float MinMultiplier = 0.5f;
        public const float MaxMultiplier = 2.0f;
        public const int MaxReasoningWords = 60;

        // Only catches injection-style patterns: executable function calls and code fences.
        // Conceptual mentions ("we must never expose private keys") in the reasoning prose are allowed.
        private static readonly Regex BannedPatterns = new Regex(
            @"(?ix)(
                eth_sendRawTransaction |
                eth_sendTransaction |
                signTransaction |
                personal_sign |
                approve\s*\( |
                transferFrom\s*\( |
                payPlayer\s*\( |
                \.\s*deposit\s*\( |
                \.\s*withdraw\s*\( |
                exec\s*\( |
                eval\s*\( |
                system\s*\( |
                <\s*script\b
            )", RegexOptions.Compiled);

        private static readonly string[] RequiredModifierFields = new[]
        {
            "livingNeedsWeightMultiplier",
            "reserveWeightMultiplier",
            "tradingWeightMultiplier",
            "minimumLivingBudgetMultiplier",
            "minimumReserveBudgetMultiplier",
            "rebalanceIntervalMultiplier",
            "chainActionCooldownMultiplier",
            "minTradeMultiplier",
            "maxTradeMultiplier"
        };

        public static MacroPolicyValidationResult Validate(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return MacroPolicyValidationResult.Fail("Empty response.");
            }

            string trimmed = ExtractJsonObject(rawText);
            if (trimmed == null)
            {
                return MacroPolicyValidationResult.Fail("No JSON object found in response.");
            }

            if (BannedPatterns.IsMatch(trimmed))
            {
                return MacroPolicyValidationResult.Fail("Response contains banned content (code/keys/direct transfer).");
            }

            JObject root;
            try
            {
                root = JObject.Parse(trimmed);
            }
            catch (JsonException ex)
            {
                return MacroPolicyValidationResult.Fail("Malformed JSON: " + ex.Message);
            }

            string reasoning = root.Value<string>("reasoning") ?? string.Empty;
            int wordCount = CountWords(reasoning);
            if (wordCount > MaxReasoningWords)
            {
                return MacroPolicyValidationResult.Fail(
                    $"reasoning exceeds {MaxReasoningWords} words (got {wordCount}).");
            }

            string targetEventStr = root.Value<string>("target_event");
            if (string.IsNullOrEmpty(targetEventStr))
            {
                return MacroPolicyValidationResult.Fail("Missing target_event.");
            }

            if (!Enum.TryParse(targetEventStr, true, out MacroTargetEvent targetEvent))
            {
                return MacroPolicyValidationResult.Fail(
                    "target_event must be one of: Normal, EnergyShortage, Inflation, MarketBoom, LiquidityCrunch. Got: " + targetEventStr);
            }

            JToken modifiersToken = root["modifiers"];
            if (modifiersToken == null || modifiersToken.Type != JTokenType.Object)
            {
                return MacroPolicyValidationResult.Fail("Missing or invalid modifiers object.");
            }

            JObject modifiers = (JObject)modifiersToken;
            MacroPolicyModifiers parsed = new MacroPolicyModifiers();

            for (int i = 0; i < RequiredModifierFields.Length; i++)
            {
                string field = RequiredModifierFields[i];
                JToken val = modifiers[field];
                if (val == null)
                {
                    return MacroPolicyValidationResult.Fail("Missing modifier field: " + field);
                }

                if (val.Type != JTokenType.Float && val.Type != JTokenType.Integer)
                {
                    return MacroPolicyValidationResult.Fail("Modifier field must be a number: " + field);
                }

                float v = val.Value<float>();
                if (float.IsNaN(v) || float.IsInfinity(v))
                {
                    return MacroPolicyValidationResult.Fail("Modifier field is not a finite number: " + field);
                }

                if (v < MinMultiplier - 1e-4f || v > MaxMultiplier + 1e-4f)
                {
                    return MacroPolicyValidationResult.Fail(
                        $"Modifier {field}={v} out of range [{MinMultiplier}, {MaxMultiplier}].");
                }

                AssignModifier(parsed, field, v);
            }

            MacroPolicy policy = new MacroPolicy
            {
                reasoning = reasoning.Trim(),
                target_event = targetEvent,
                modifiers = parsed,
                rawJson = trimmed,
                appliedUtc = DateTime.UtcNow.ToString("o")
            };

            return MacroPolicyValidationResult.Ok(policy);
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string[] parts = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length;
        }

        private static string ExtractJsonObject(string text)
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            return text.Substring(start, end - start + 1);
        }

        private static void AssignModifier(MacroPolicyModifiers m, string field, float value)
        {
            switch (field)
            {
                case "livingNeedsWeightMultiplier": m.livingNeedsWeightMultiplier = value; break;
                case "reserveWeightMultiplier": m.reserveWeightMultiplier = value; break;
                case "tradingWeightMultiplier": m.tradingWeightMultiplier = value; break;
                case "minimumLivingBudgetMultiplier": m.minimumLivingBudgetMultiplier = value; break;
                case "minimumReserveBudgetMultiplier": m.minimumReserveBudgetMultiplier = value; break;
                case "rebalanceIntervalMultiplier": m.rebalanceIntervalMultiplier = value; break;
                case "chainActionCooldownMultiplier": m.chainActionCooldownMultiplier = value; break;
                case "minTradeMultiplier": m.minTradeMultiplier = value; break;
                case "maxTradeMultiplier": m.maxTradeMultiplier = value; break;
            }
        }
    }
}

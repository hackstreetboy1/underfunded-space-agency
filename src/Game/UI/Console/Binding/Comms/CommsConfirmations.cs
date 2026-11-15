using Usa.Game.Comms;
using Usa.Game.Content;

namespace Usa.Game.UI.Console.Binding.Comms;

/// <summary>Turns a comms confirmation (a high-impact command) into the console dialog's prompt.</summary>
public static class CommsConfirmations
{
    /// <param name="text">Resolves the effect text keys (<c>impact.*</c> in <c>content/strings.json</c>).</param>
    public static ConfirmPrompt ToPrompt(ConfirmationRequest request, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(text);
        var effects = new List<string>(request.EffectTextKeys.Count);
        foreach (var key in request.EffectTextKeys)
        {
            // A missing key must not block a safety prompt: show the key rather than nothing.
            try
            {
                effects.Add(text.Get(key));
            }
            catch (KeyNotFoundException)
            {
                effects.Add(key);
            }
        }
        return new ConfirmPrompt(ConfirmPromptKind.Command, request.Title, effects, request.Irreversible, request.ExecutorName);
    }
}

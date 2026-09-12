using QuickMarkup.Infra.Collections;
using UnoVibe.Integration;
using UnoVibe.Models;

namespace UnoVibe.Providers;

public class ModelsProvider
{
    public required OpencodeClient Opencode { private get; init; }
    public required ToastService Toasts { private get; init; }
    public ReactiveList<string> AgentOptions { get; } = [];
    public ReactiveKeyedSet<Model, ModelOption> ModelOptions { get; } = new(Model.From);

    public ModelsProvider()
    {
        _ = RefreshModelsAsync();
    }

    /// <summary>
    /// Refreshes the shared mode/model option lists and re-applies the active session's
    /// selections (used as defaults for a new draft chat).
    /// </summary>
    public async Task RefreshModelsAsync(CancellationToken ct = default)
    {
        try
        {
            if (!(await Opencode.GetAgentsAsync(ct)).TryGetValue(out var agents, out var error))
            {
                Toasts.ShowError(error, "Could not load agent modes");
                return;
            }
            AgentOptions.Clear();
            foreach (var agent in agents)
            {
                if (agent.Mode != "primary") continue;
                if (agent.Hidden) continue;
                var name = agent.Name;
                if (name.Length > 0 && !AgentOptions.Contains(name)) AgentOptions.Add(name);
            }
            // if (Active.Mode.Length == 0 || !AgentOptions.Contains(Active.Mode)) Active.Mode = "build";

            if (!(await Opencode.GetProvidersAsync(ct)).TryGetValue(out var providers, out var error1))
            {
                Toasts.ShowError(error1, "Could not load models");
                return;
            }
            ModelOptions.Clear();
            
            if (providers.Connected is null)
            {
                Toasts.ShowError("Could not load models: Connected provider information is not avaliable");
                return;
            }
            if (providers.All is null)
            {
                Toasts.ShowError("Could not load models: Model information is not avaliable");
                return;
            }
            var connectedIds = new HashSet<string>(providers.Connected);

            foreach (var provider in providers.All)
            {
                if (!connectedIds.Contains(provider.Id)) continue;
                if (provider.Models is null) continue;
                foreach (var (id, model) in provider.Models)
                {
                    var variants = new List<string>();
                    if (model.Variants is null) continue;
                    foreach (var variant in model.Variants.Keys) variants.Add(variant);
                    var name = model.Name;
                    ModelOptions.Add(new ModelOption
                    {
                        ProviderId = provider.Id,
                        Id = id,
                        Name = model.Name.Length > 0 ? model.Name : id,
                        Variants = [.. variants],
                        LimitContext = model.Limit?.Context ?? 0,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Could not load agents or models");
        }
    }
}
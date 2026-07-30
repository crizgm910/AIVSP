using Aivsp.Application.Configuration;

namespace Aivsp.Application.Contracts;

public interface IAssistantSettingsStore
{
    void LoadInto(AssistantOptions options);
    void Save(AssistantOptions options);
}

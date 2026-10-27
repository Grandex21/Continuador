using System;
using System.Collections.Generic;
using System.Linq;

namespace Continuador
{
    internal static class ConversationPreferences
    {
        public static void Apply(IList<ConversationRecord> conversations)
        {
            AppState state = StateStore.Load();
            Dictionary<string, ConversationPreference> preferences = state.Settings.Conversations
                .Where(delegate(ConversationPreference item) { return item != null && !string.IsNullOrWhiteSpace(item.Key); })
                .GroupBy(delegate(ConversationPreference item) { return item.Key; }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(delegate(IGrouping<string, ConversationPreference> group) { return group.Key; },
                    delegate(IGrouping<string, ConversationPreference> group) { return group.Last(); },
                    StringComparer.OrdinalIgnoreCase);
            foreach (ConversationRecord conversation in conversations)
            {
                ConversationPreference preference;
                if (preferences.TryGetValue(Key(conversation.Client, conversation.SessionId), out preference))
                {
                    conversation.Favorite = preference.Favorite;
                    conversation.CustomName = preference.CustomName;
                }
            }
        }

        public static bool ToggleFavorite(ConversationRecord conversation)
        {
            return StateStore.Transaction<bool>(delegate(AppState state)
            {
                ConversationPreference preference = GetOrCreate(state, conversation);
                preference.Favorite = !preference.Favorite;
                conversation.Favorite = preference.Favorite;
                RemoveIfEmpty(state, preference);
                return conversation.Favorite;
            }, true);
        }

        public static void SetCustomName(ConversationRecord conversation, string name)
        {
            StateStore.Transaction<object>(delegate(AppState state)
            {
                ConversationPreference preference = GetOrCreate(state, conversation);
                preference.CustomName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                conversation.CustomName = preference.CustomName;
                RemoveIfEmpty(state, preference);
                return null;
            }, true);
        }

        private static ConversationPreference GetOrCreate(AppState state, ConversationRecord conversation)
        {
            string key = Key(conversation.Client, conversation.SessionId);
            ConversationPreference preference = state.Settings.Conversations.FirstOrDefault(delegate(ConversationPreference item)
            {
                return item != null && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
            });
            if (preference == null)
            {
                preference = new ConversationPreference { Key = key };
                state.Settings.Conversations.Add(preference);
            }
            return preference;
        }

        private static void RemoveIfEmpty(AppState state, ConversationPreference preference)
        {
            if (!preference.Favorite && string.IsNullOrWhiteSpace(preference.CustomName))
            {
                state.Settings.Conversations.Remove(preference);
            }
        }

        private static string Key(string client, string session)
        {
            return (client ?? string.Empty).ToLowerInvariant() + "|" + (session ?? string.Empty).ToLowerInvariant();
        }
    }
}

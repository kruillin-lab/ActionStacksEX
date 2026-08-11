using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ActionStacksEX;

public class Configuration : PluginConfiguration, IPluginConfiguration
{
    public class Action
    {
        public uint ID = 0;
        public bool UseAdjustedID = false;
    }

    public class ActionStackItem
    {
        public uint ID = 0;
        public uint TargetID = 10_000;
        public bool Enabled = true;
        public float HpRatio = 1.0f;

        /// <summary>
        /// Statuses this item checks, matched any-of. With <see cref="MissingStatus"/> the item
        /// requires none of them present; otherwise it requires at least one. Listing the
        /// level-scaled forms of one buff (Aspected Helios and Helios Conjunction, say) guards
        /// every sync level, since only the level-appropriate form can ever be applied.
        /// Empty disables the check.
        /// </summary>
        public List<uint> StatusIDs = [];

        public bool MissingStatus = false;

        /// <summary>
        /// Absorbs the pre-list <c>StatusID</c> field so saved configs and previously exported
        /// stacks keep their check. Write-only, so it is never serialised back out.
        /// </summary>
        [JsonProperty("StatusID")]
        private uint LegacyStatusID
        {
            set
            {
                if (value != 0 && !StatusIDs.Contains(value))
                    StatusIDs.Add(value);
            }
        }
    }

    public class ActionStack
    {
        public string Name = string.Empty;
        public uint TriggerAction = 0;
        public bool UseAdjustedTrigger = false;
        public List<ActionStackItem> Items = [];
        public uint ModifierKeys = 0u;
        public bool BlockOriginal = false;
        public bool CheckRange = false;
        public bool CheckCooldown = false;
    }

    public class StackSerializer : DefaultSerializationBinder
    {
        private static readonly Type actionStackType = typeof(ActionStack);
        private static readonly Type actionStackItemType = typeof(ActionStackItem);
        private static readonly Type actionType = typeof(Action);
        private const string actionStackShortName = "s";
        private const string actionStackItemShortName = "i";
        private const string actionShortName = "a";
        private static readonly Dictionary<string, Type> types = new()
        {
            [actionStackType.FullName!] = actionStackType,
            [actionStackShortName] = actionStackType,
            [actionStackItemType.FullName!] = actionStackItemType,
            [actionStackItemShortName] = actionStackItemType,
            [actionType.FullName!] = actionType,
            [actionShortName] = actionType
        };
        private static readonly Dictionary<Type, string> typeNames = new()
        {
            [actionStackType] = actionStackShortName,
            [actionStackItemType] = actionStackItemShortName,
            [actionType] = actionShortName
        };

        public override Type BindToType(string assemblyName, string typeName)
            => types.TryGetValue(typeName, out var t) ? t : base.BindToType(assemblyName, typeName);

        public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = null;
            if (typeNames.TryGetValue(serializedType, out var name))
                typeName = name;
            else
                base.BindToName(serializedType, out assemblyName, out typeName);
        }
    }

    public int Version { get; set; }

    public List<ActionStack> ActionStacks = [];

    /// <summary>
    /// How long (ms) a successful stack suppresses re-evaluation of the same trigger.
    /// This exists to stop one keypress firing several stack items, so it must stay well
    /// below a GCD — above ~2500ms it swallows genuine repeat presses and the stack's
    /// conditions stop being checked at all.
    /// </summary>
    public int StackReentrancyWindow = 600;
    public bool EnableEnhancedAutoFaceTarget = false;
    public bool EnableAutoDismount = false;
    public bool EnableGroundTargetQueuing = false;
    public bool EnableInstantGroundTarget = false;
    public bool EnableBlockMiscInstantGroundTargets = false;
    public bool EnableAutoCastCancel = false;
    public bool EnableAutoTarget = false;
    public bool EnableAutoChangeTarget = false;
    public bool EnableSpellAutoAttacks = false;
    public bool EnableSpellAutoAttacksOutOfCombat = false;
    public bool EnableCameraRelativeDashes = false;
    public bool EnableNormalBackwardDashes = false;
    public bool EnableReverseBackwardDashes = false;
    public bool EnableQueuingMore = false;
    public bool EnableFrameAlignment = false;
    public bool EnableAutoRefocusTarget = false;
    public bool EnableMacroQueue = false;
    public bool EnableFractionality = false;
    public bool EnablePlayerNamesInCommands = false;
    public bool EnableQueueAdjustments = false;
    public bool EnableRequeuing = false;
    public bool EnableGCDAdjustedQueueThreshold = false;
    public float QueueThreshold = 0.5f;
    public float QueueLockThreshold = 0.5f;
    public float QueueActionLockout = 0f;
    public bool EnableTurboHotbars = false;
    public int TurboHotbarInterval = 400;
    public int InitialTurboHotbarInterval = 0;
    public bool EnableTurboHotbarsOutOfCombat = false;
    public bool ToggleTurboMode = false;
    public bool EnableCameraRelativeDirectionals = false;
    public bool EnableUnassignableActions = false;
    public uint AutoFocusTargetID = 0;
    public bool EnableAutoFocusTargetOutOfCombat = false;

    public bool EnableDecomboMeditation = false;
    public bool EnableDecomboBunshin = false;
    public bool EnableDecomboWanderersMinuet = false;
    public bool EnableDecomboLiturgy = false;
    public bool EnableDecomboEarthlyStar = false;
    public bool EnableDecomboMinorArcana = false;
    public bool EnableDecomboGeirskogul = false;
    public bool IgnoreCamera = false;

    public override void Initialize() { }

    private static readonly StackSerializer serializer = new ();

    private const string exportPrefix = "ASEX_";

    public static string ExportActionStack(ActionStack stack)
        => Util.CompressString(JsonConvert.SerializeObject(stack, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            SerializationBinder = serializer
        }), exportPrefix);

    public static ActionStack ImportActionStack(string import)
        => JsonConvert.DeserializeObject<ActionStack>(Util.DecompressString(import, exportPrefix), new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            SerializationBinder = serializer
        });
}
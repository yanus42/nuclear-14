using Content.Server.Audio;
using Content.Server.Power.Generator;
using Content.Shared._Forge.ResourceExtractor;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ResourceExtractor;

public sealed class ResourceExtractorSystem : SharedResourceExtractorSystem
{
    [Dependency] private readonly ResourceExtractorFuelSystem _fuel = default!;
    [Dependency] private readonly ResourceExtractorOutputSystem _output = default!;
    [Dependency] private readonly ResourceExtractorSpawnEventSystem _spawnEvents = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AmbientSoundSystem _ambient = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private bool _invalidSettingsLogged;

    private ResourceExtractorSettingsPrototype Settings =>
        _prototypes.Index<ResourceExtractorSettingsPrototype>(ResourceExtractorSettingsPrototype.DefaultId);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ResourceExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ResourceExtractorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ResourceExtractorComponent, EntRemovedFromContainerMessage>(OnOutputRemoved);
        SubscribeLocalEvent<ResourceExtractorComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ResourceExtractorComponent, ResourceExtractorStartMessage>(OnStartMessage);
        SubscribeLocalEvent<ResourceExtractorComponent, ResourceExtractorStopMessage>(OnStopMessage);
        SubscribeLocalEvent<ResourceExtractorComponent, ResourceExtractorEjectFuelMessage>(OnEjectFuelMessage);
        SubscribeLocalEvent<ResourceExtractorComponent, ResourceExtractorOpenOutputMessage>(OnOpenOutputMessage);
    }

    private void OnMapInit(EntityUid uid, ResourceExtractorComponent component, MapInitEvent args)
    {
        component.UserEnabled = false;

        if (!ValidateSettings(Settings) || !ValidateConfiguration(uid, component, Settings))
        {
            SetStopped(uid, component, ResourceExtractorStatus.InvalidConfiguration, clearUserLatch: true);
            return;
        }

        SetStopped(uid, component, ResourceExtractorStatus.Off, clearUserLatch: true);
    }

    private bool ValidateSettings(ResourceExtractorSettingsPrototype settings)
    {
        if (settings.MaxOutputOperationsPerTick > 0 &&
            settings.MaxAllowedBatchSize > 0 &&
            settings.MaxSpawnedEntitiesPerTick >= settings.MaxAllowedBatchSize &&
            settings.MaxEventsPerProduction >= 0 &&
            settings.MaxEventSpawnsPerProduction > 0 &&
            settings.MaxEventOperationsPerTick > 0 &&
            settings.MaxEventSpawnRadius > 0 &&
            settings.MaxTelegraphDuration > TimeSpan.Zero &&
            settings.MinExtractionDuration > TimeSpan.Zero &&
            settings.OutputRetryPeriod > TimeSpan.Zero &&
            settings.UiUpdatePeriod > TimeSpan.Zero &&
            float.IsFinite(settings.FuelEpsilon) && settings.FuelEpsilon >= 0f)
        {
            return true;
        }

        if (!_invalidSettingsLogged)
        {
            Log.Error("Resource extractor settings are invalid. Extractors will remain disabled until the YAML is fixed.");
            _invalidSettingsLogged = true;
        }

        return false;
    }

    private bool ValidateConfiguration(
        EntityUid uid,
        ResourceExtractorComponent component,
        ResourceExtractorSettingsPrototype settings)
    {
        if (component.ExtractionDuration < settings.MinExtractionDuration ||
            !float.IsFinite(component.FuelConsumptionRate) ||
            component.FuelConsumptionRate <= 0f ||
            !HasComp<StorageComponent>(uid))
        {
            Log.Error($"Invalid resource extractor configuration on {ToPrettyString(uid)}: invalid machine values.");
            return false;
        }

        if (!_output.ValidateProduction(component.Production, settings.MaxAllowedBatchSize, out var productionError))
        {
            Log.Error($"Invalid resource extractor configuration on {ToPrettyString(uid)}: {productionError}.");
            return false;
        }

        if (!_fuel.TryGetFuelFraction(uid, out _))
        {
            Log.Error($"Resource extractor {ToPrettyString(uid)} must have exactly one supported fuel adapter with a finite positive capacity.");
            return false;
        }

        var production = _prototypes.Index<ResourceExtractorProductionPrototype>(component.Production);
        if (!_spawnEvents.ValidateEvents(production, out var eventError))
        {
            Log.Error($"Invalid resource extractor configuration on {ToPrettyString(uid)}: {eventError}.");
            return false;
        }

        return true;
    }

    private void OnAnchorChanged(EntityUid uid, ResourceExtractorComponent component, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            if (component.Status == ResourceExtractorStatus.Unanchored)
                component.Status = ResourceExtractorStatus.Off;
            UpdateUi(uid, component);
            return;
        }

        SetStopped(uid, component, ResourceExtractorStatus.Unanchored, clearUserLatch: true);
    }

    private void OnOutputRemoved(EntityUid uid, ResourceExtractorComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ResourceExtractorComponent.OutputContainerId)
            return;

        if (TryComp<PendingResourceExtractorOperationComponent>(uid, out var pending) && pending.Batch.Count > 0)
        {
            // Pending transactions may delete their own temporary entities
            // while rolling back a batch that did not fully fit. A bounded
            // periodic retry distinguishes that cleanup from player actions.
            UpdateUi(uid, component);
            return;
        }

        if (component.UserEnabled)
        {
            ResumeMachine(uid, component);
        }

        UpdateUi(uid, component);
    }

    private void OnUiOpened(EntityUid uid, ResourceExtractorComponent component, BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(ResourceExtractorUiKey.Key))
            return;

        UpdateUi(uid, component, force: true);
    }

    private void OnStartMessage(EntityUid uid, ResourceExtractorComponent component, ResourceExtractorStartMessage args)
    {
        TryStart(uid, component);
    }

    private void OnStopMessage(EntityUid uid, ResourceExtractorComponent component, ResourceExtractorStopMessage args)
    {
        SetStopped(uid, component, ResourceExtractorStatus.Off, clearUserLatch: true);
    }

    private void OnEjectFuelMessage(
        EntityUid uid,
        ResourceExtractorComponent component,
        ResourceExtractorEjectFuelMessage args)
    {
        component.UserEnabled = false;
        _fuel.EmptyFuel(uid);
        SetMotorStopped(uid, component, ResourceExtractorStatus.NoFuel);
    }

    private void OnOpenOutputMessage(
        EntityUid uid,
        ResourceExtractorComponent component,
        ResourceExtractorOpenOutputMessage args)
    {
        if (!TryComp<StorageComponent>(uid, out var storage))
            return;

        // OpenStorageUI does not enforce the global one-storage-window limit when
        // called directly instead of through SharedStorageSystem's activation handler.
        _ui.CloseUserUis<StorageComponent.StorageUiKey>(args.Actor);
        _storage.OpenStorageUI(uid, args.Actor, storage, silent: false);
    }

    public bool TryStart(EntityUid uid, ResourceExtractorComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        var settings = Settings;
        if (!ValidateSettings(settings) || !ValidateConfiguration(uid, component, settings))
        {
            SetStopped(uid, component, ResourceExtractorStatus.InvalidConfiguration, clearUserLatch: true);
            return false;
        }

        var hasPendingOutput = TryComp<PendingResourceExtractorOperationComponent>(uid, out var pending) &&
                               pending.Batch.Count > 0;
        if (GetBlockingStatus(uid, component, checkOutput: !hasPendingOutput) is { } blockedStatus)
        {
            // A full output is a resumable pause, unlike missing fuel, a clog,
            // or an unanchored machine. Keep the user's start request latched.
            if (blockedStatus == ResourceExtractorStatus.OutputFull)
                component.UserEnabled = true;

            SetStopped(uid, component, blockedStatus, clearUserLatch: blockedStatus != ResourceExtractorStatus.OutputFull);
            return blockedStatus == ResourceExtractorStatus.OutputFull;
        }

        component.UserEnabled = true;
        ResumeMachine(uid, component);
        return true;
    }

    private void ResumeMachine(EntityUid uid, ResourceExtractorComponent component)
    {
        if (HasComp<PendingResourceExtractorOperationComponent>(uid))
        {
            component.Status = ResourceExtractorStatus.ProcessingOutput;
            UpdateUi(uid, component);
            return;
        }

        if (GetBlockingStatus(uid, component) is { } blockedStatus)
        {
            SetStopped(uid, component, blockedStatus, clearUserLatch: blockedStatus != ResourceExtractorStatus.OutputFull);
            return;
        }

        SetMotorRunning(uid, component);
    }

    private ResourceExtractorStatus? GetBlockingStatus(
        EntityUid uid,
        ResourceExtractorComponent component,
        bool checkOutput = true)
    {
        if (!Transform(uid).Anchored)
            return ResourceExtractorStatus.Unanchored;
        if (_fuel.IsClogged(uid))
            return ResourceExtractorStatus.Clogged;
        if (!_fuel.HasFuel(uid, Settings.FuelEpsilon))
            return ResourceExtractorStatus.NoFuel;
        if (checkOutput && !_output.HasAnyCapacity(uid))
            return ResourceExtractorStatus.OutputFull;

        return null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var settings = Settings;
        if (!ValidateSettings(settings))
            return;

        // Finish previously queued output work first. A cycle queued below is
        // intentionally not processed until the next tick, keeping expensive
        // entity creation and storage insertion out of the active timer loop.
        ProcessPendingOutputOperations(settings);
        UpdateActiveExtractors(frameTime, settings);
    }

    private void UpdateActiveExtractors(float frameTime, ResourceExtractorSettingsPrototype settings)
    {
        var query = EntityQueryEnumerator<ActiveResourceExtractorComponent, ResourceExtractorComponent>();
        while (query.MoveNext(out var uid, out _, out var component))
        {
            // Active marker removal is deferred to keep this query stable.
            // Status prevents that one-tick-old marker from doing extra work.
            if (!component.UserEnabled || component.Status != ResourceExtractorStatus.Running)
                continue;

            // Fuel and clogging are checked by TryConsumeFuelForDuration below.
            // Avoid resolving the solution several additional times per active
            // extractor and tick; those lookups dominate the cheap timer math.
            if (!Transform(uid).Anchored)
            {
                SetStopped(uid, component, ResourceExtractorStatus.Unanchored, clearUserLatch: true);
                continue;
            }

            if (!_output.HasAnyCapacity(uid))
            {
                SetMotorStopped(uid, component, ResourceExtractorStatus.OutputFull);
                continue;
            }

            var remaining = component.ExtractionDuration - component.ExtractionProgress;
            if (remaining <= TimeSpan.Zero)
            {
                QueueOutputOperation(uid, component);
                continue;
            }

            var requestedSeconds = Math.Min(frameTime, remaining.TotalSeconds);
            var result = _fuel.TryConsumeFuelForDuration(
                uid,
                TimeSpan.FromSeconds(requestedSeconds),
                component.FuelConsumptionRate,
                settings.FuelEpsilon);

            component.ExtractionProgress += result.WorkedDuration;
            if (component.ExtractionProgress > component.ExtractionDuration)
                component.ExtractionProgress = component.ExtractionDuration;

            if (component.ExtractionProgress >= component.ExtractionDuration)
                QueueOutputOperation(uid, component);

            switch (result.Status)
            {
                case FuelConsumptionStatus.Exhausted:
                    component.UserEnabled = false;
                    SetMotorStopped(uid, component, ResourceExtractorStatus.NoFuel);
                    break;
                case FuelConsumptionStatus.Clogged:
                    component.UserEnabled = false;
                    SetMotorStopped(uid, component, ResourceExtractorStatus.Clogged);
                    break;
                case FuelConsumptionStatus.InvalidConfiguration:
                    component.UserEnabled = false;
                    SetMotorStopped(uid, component, ResourceExtractorStatus.InvalidConfiguration);
                    break;
            }

            component.UiUpdateAccumulator += frameTime;
            var uiUpdatePeriod = (float) settings.UiUpdatePeriod.TotalSeconds;
            if (component.UiUpdateAccumulator >= uiUpdatePeriod)
            {
                component.UiUpdateAccumulator %= uiUpdatePeriod;
                UpdateUi(uid, component);
            }
        }
    }

    private void QueueOutputOperation(EntityUid uid, ResourceExtractorComponent component)
    {
        component.ExtractionProgress = component.ExtractionDuration;
        // Do not stop the motor here. A successful output commit immediately
        // begins the next cycle, so toggling the appearance for one tick makes
        // the diesel animation visibly jerk at every completed batch.
        component.Status = ResourceExtractorStatus.ProcessingOutput;
        EnsureComp<PendingResourceExtractorOperationComponent>(uid);
        UpdateUi(uid, component);
    }

    private void ProcessPendingOutputOperations(ResourceExtractorSettingsPrototype settings)
    {
        var remainingOperations = settings.MaxOutputOperationsPerTick;
        var remainingEntities = settings.MaxSpawnedEntitiesPerTick;

        // Output commits spawn entities, mutate storage, and remove marker
        // components. Snapshot first so none of those transactions invalidate
        // the component dictionaries currently being enumerated.
        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<PendingResourceExtractorOperationComponent, ResourceExtractorComponent>();
        while (query.MoveNext(out var uid, out _, out _))
            candidates.Add(uid);

        foreach (var uid in candidates)
        {
            if (remainingOperations <= 0)
                break;

            if (!TryComp<PendingResourceExtractorOperationComponent>(uid, out var pending) ||
                !TryComp<ResourceExtractorComponent>(uid, out var component))
                continue;

            if (pending.WaitingForCapacity && _timing.CurTime < pending.NextCapacityRetry)
                continue;

            // Count attempts rather than only successful commits. Invalid
            // tables and full outputs must not bypass the global work budget.
            remainingOperations--;

            if (pending.WaitingForCapacity)
            {
                pending.NextCapacityRetry = _timing.CurTime + settings.OutputRetryPeriod;
                if (!_output.HasAnyCapacity(uid))
                    continue;

                pending.WaitingForCapacity = false;
                component.Status = ResourceExtractorStatus.ProcessingOutput;
            }

            if (pending.Batch.Count == 0)
            {
                if (!_output.TryCreateBatch(
                        uid,
                        component.Production,
                        pending.Batch,
                        out var error))
                {
                    Log.Error($"Resource extractor output failed on {ToPrettyString(uid)}: {error}.");
                    SetStopped(uid, component, ResourceExtractorStatus.InvalidConfiguration, clearUserLatch: true);
                    RemComp<PendingResourceExtractorOperationComponent>(uid);
                    continue;
                }

            }

            // Conservative accounting: merged stacks cost less in practice,
            // but counting every table result gives a simple hard upper bound.
            if (pending.Batch.Count > remainingEntities)
                continue;

            remainingEntities -= pending.Batch.Count;

            switch (_output.TryCommitBatch(uid, pending.Batch))
            {
                case ResourceExtractorCommitResult.OutputFull:
                    pending.WaitingForCapacity = true;
                    pending.NextCapacityRetry = _timing.CurTime + settings.OutputRetryPeriod;
                    SetMotorStopped(uid, component, ResourceExtractorStatus.OutputFull);
                    continue;
                case ResourceExtractorCommitResult.InvalidConfiguration:
                    pending.Batch.Clear();
                    SetStopped(uid, component, ResourceExtractorStatus.InvalidConfiguration, clearUserLatch: true);
                    RemComp<PendingResourceExtractorOperationComponent>(uid);
                    continue;
            }

            pending.Batch.Clear();
            component.ExtractionProgress = TimeSpan.Zero;
            RemComp<PendingResourceExtractorOperationComponent>(uid);
            _spawnEvents.RollEvents(
                uid,
                _prototypes.Index<ResourceExtractorProductionPrototype>(component.Production));

            if (component.UserEnabled)
                ResumeMachine(uid, component);
            else if (!component.UserEnabled && component.Status == ResourceExtractorStatus.ProcessingOutput)
                component.Status = ResourceExtractorStatus.Off;

            UpdateUi(uid, component);
        }
    }

    private void SetMotorRunning(EntityUid uid, ResourceExtractorComponent component)
    {
        EnsureComp<ActiveResourceExtractorComponent>(uid);
        component.Status = ResourceExtractorStatus.Running;
        _appearance.SetData(uid, ResourceExtractorVisuals.Running, true);
        _ambient.SetAmbience(uid, true);
        UpdateUi(uid, component);
    }

    private void SetMotorStopped(
        EntityUid uid,
        ResourceExtractorComponent component,
        ResourceExtractorStatus status)
    {
        RemCompDeferred<ActiveResourceExtractorComponent>(uid);
        component.Status = status;
        _appearance.SetData(uid, ResourceExtractorVisuals.Running, false);
        _ambient.SetAmbience(uid, false);
        UpdateUi(uid, component);
    }

    private void SetStopped(
        EntityUid uid,
        ResourceExtractorComponent component,
        ResourceExtractorStatus status,
        bool clearUserLatch)
    {
        if (clearUserLatch)
            component.UserEnabled = false;

        SetMotorStopped(uid, component, status);
    }

    private void UpdateUi(EntityUid uid, ResourceExtractorComponent component, bool force = false)
    {
        if (!force && !_ui.IsUiOpen(uid, ResourceExtractorUiKey.Key))
            return;

        var pendingCount = TryComp<PendingResourceExtractorOperationComponent>(uid, out var pending)
            ? pending.Batch.Count
            : 0;

        if (!_fuel.TryGetFuelFraction(uid, out var fuelFraction))
            fuelFraction = 0f;

        _ui.SetUiState(
            uid,
            ResourceExtractorUiKey.Key,
            new ResourceExtractorUiState(
                component.Status,
                component.UserEnabled,
                fuelFraction,
                (float) component.ExtractionProgress.TotalSeconds,
                (float) component.ExtractionDuration.TotalSeconds,
                pendingCount));
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

public class NpcDemoUiDataSource : MonoBehaviour
{
    [Header("Follow")] [SerializeField] private Camera followCamera;
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 5f, -7f);
    [SerializeField] private float followLerpSpeed = 8f;
    [SerializeField] private bool rotateCameraToNpc = true;

    [Header("Refresh")] [SerializeField] private int maxActivitiesPerNpc = 30;

    private TradingNpcActor selectedNpc;
    private bool followSelectedNpc;

    public event Action<List<NpcListEntry>> NpcListChanged;
    public event Action<TradingNpcSnapshot> SelectedNpcChanged;
    public event Action<TradingNpcSnapshot> SelectedNpcSnapshotRefreshed;

    public TradingNpcActor SelectedNpc
    {
        get { return selectedNpc; }
    }

    public TradingNpcSnapshot SelectedSnapshot
    {
        get { return selectedNpc != null ? selectedNpc.CreateSnapshot(maxActivitiesPerNpc) : null; }
    }

    private void Awake()
    {
        if (followCamera == null)
        {
            followCamera = Camera.main;
        }
    }

    private void Start()
    {
        RefreshNpcList();
    }

    private void LateUpdate()
    {
        if (followSelectedNpc && selectedNpc != null && followCamera != null)
        {
            Vector3 targetPosition = selectedNpc.transform.position + followOffset;
            FadeObjectBlockingObject.Instance.TargetTransform = selectedNpc.transform;
            followCamera.transform.position = Vector3.Lerp(
                followCamera.transform.position,
                targetPosition,
                Time.deltaTime * followLerpSpeed);

            if (rotateCameraToNpc)
            {
                Vector3 lookDirection = selectedNpc.transform.position - followCamera.transform.position;
                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    followCamera.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                }
            }
        }
    }

    public List<NpcListEntry> RefreshNpcList()
    {
        List<NpcListEntry> entries = BuildNpcListEntries();
        NpcListChanged?.Invoke(entries);
        return entries;
    }

    public List<NpcListEntry> BuildNpcListEntries()
    {
        List<NpcListEntry> entries = new List<NpcListEntry>();
        List<TradingNpcActor> npcs = GetAllTradingNpcs();

        for (int i = 0; i < npcs.Count; i++)
        {
            TradingNpcActor npc = npcs[i];
            if (npc == null)
            {
                continue;
            }

            entries.Add(new NpcListEntry
            {
                npcId = npc.GetInstanceID().ToString(),
                displayName = npc.name,
                archetype = npc.archetype,
                walletAddress = npc.WalletAddress,
                currentActivity = npc.CurrentActivity,
                totalUSDC = npc.portfolioState.TotalUSDC,
                npc = npc
            });
        }

        return entries;
    }

    public TradingNpcSnapshot SelectNpc(TradingNpcActor npc)
    {
        selectedNpc = npc;
        TradingNpcSnapshot snapshot = SelectedSnapshot;
        SelectedNpcChanged?.Invoke(snapshot);
        return snapshot;
    }

    public TradingNpcSnapshot SelectNpcById(string npcId)
    {
        List<TradingNpcActor> npcs = GetAllTradingNpcs();
        for (int i = 0; i < npcs.Count; i++)
        {
            TradingNpcActor npc = npcs[i];
            if (npc != null && npc.GetInstanceID().ToString() == npcId)
            {
                return SelectNpc(npc);
            }
        }

        return null;
    }

    public TradingNpcSnapshot RefreshSelectedNpcDetails()
    {
        TradingNpcSnapshot snapshot = SelectedSnapshot;
        SelectedNpcSnapshotRefreshed?.Invoke(snapshot);
        return snapshot;
    }

    public void SetFollowSelectedNpc(bool enabled)
    {
        followSelectedNpc = enabled;
    }

    public void ToggleFollowSelectedNpc()
    {
        followSelectedNpc = !followSelectedNpc;
    }

    public void FocusSelectedNpcOnce()
    {
        if (selectedNpc == null || followCamera == null)
        {
            return;
        }

        followCamera.transform.position = selectedNpc.transform.position + followOffset;
        Vector3 lookDirection = selectedNpc.transform.position - followCamera.transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            followCamera.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
    }

    private List<TradingNpcActor> GetAllTradingNpcs()
    {
        if (ActorManager.Instance != null)
        {
            return ActorManager.Instance.GetAllActorsByType<TradingNpcActor>();
        }

        return new List<TradingNpcActor>(FindObjectsOfType<TradingNpcActor>());
    }
}

[Serializable]
public class NpcListEntry
{
    public string npcId;
    public string displayName;
    public TradingNpcArchetype archetype;
    public string walletAddress;
    public string currentActivity;
    public float totalUSDC;
    public TradingNpcActor npc;
}
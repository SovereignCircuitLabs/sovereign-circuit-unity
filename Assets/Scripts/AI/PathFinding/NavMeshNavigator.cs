using UnityEngine;
using UnityEngine.AI;

public class NavMeshNavigator : MonoBehaviour
{
    [Header("Path Following")] [SerializeField]
    private float cornerReachedDistance = 0.4f;

    [SerializeField] private float arrivedDistance = 0.3f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private int areaMask = NavMesh.AllAreas;

    [Header("Repath")] [SerializeField] private float repathInterval = 0.5f;
    [SerializeField] private float destinationMoveThreshold = 0.5f;

    [Header("Debug")] [SerializeField] private bool debugPath = false;

    [SerializeField] private float stuckCheckInterval = 1f;
    [SerializeField] private float stuckMoveThreshold = 0.08f;

    [Header("Boundary Avoidance")] [SerializeField]
    private bool pushAwayFromNavMeshEdge = true;

    [SerializeField] private float minEdgeDistance = 0.8f;
    [SerializeField] private float edgePushDistance = 0.7f;

    private NavMeshPath path;
    private Vector3[] corners = System.Array.Empty<Vector3>();
    private int currentCornerIndex;
    private Vector3 destination;
    private bool hasDestination;
    private float lastRepathTime = -999f;
    private Vector3 lastStuckCheckPos;
    private float lastStuckCheckTime;
    private Rigidbody cachedRigidbody;

    public bool HasPath
    {
        get { return corners != null && corners.Length > 0 && currentCornerIndex < corners.Length; }
    }

    public bool IsPathComplete
    {
        get { return path.status == NavMeshPathStatus.PathComplete; }
    }

    public Vector3 FinalDestination
    {
        get { return hasDestination ? destination : transform.position; }
    }

    public bool HasReachedDestination
    {
        get
        {
            if (!hasDestination) return false;
            if (HasPath) return false;
            return (transform.position - destination).sqrMagnitude <= arrivedDistance * arrivedDistance;
        }
    }

    public Vector3 currentCornerPosition
    {
        get
        {
            if (!HasPath) return transform.position;

            Vector3 corner = corners[currentCornerIndex];

            if (pushAwayFromNavMeshEdge)
            {
                corner = PushPointAwayFromNavMeshEdge(corner);
            }

            return corner;
        }
    }

    private Vector3 PushPointAwayFromNavMeshEdge(Vector3 point)
    {
        NavMeshHit hit;

        if (!NavMesh.SamplePosition(point, out hit, navMeshSampleRadius, areaMask))
            return point;

        Vector3 sampledPoint = hit.position;

        if (!NavMesh.FindClosestEdge(sampledPoint, out hit, areaMask))
            return sampledPoint;

        if (hit.distance >= minEdgeDistance)
            return sampledPoint;

        Vector3 pushedPoint = sampledPoint + hit.normal * edgePushDistance;

        NavMeshHit pushedHit;
        if (NavMesh.SamplePosition(pushedPoint, out pushedHit, navMeshSampleRadius, areaMask))
        {
            return pushedHit.position;
        }

        return sampledPoint;
    }

    void Awake()
    {
        path = new NavMeshPath();
        cachedRigidbody = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (!HasPath) return;

        Vector3 self = transform.position;
        self.y = 0;

        while (HasPath)
        {
            Vector3 corner = corners[currentCornerIndex];
            corner.y = 0f;

            if ((corner - self).sqrMagnitude > cornerReachedDistance * cornerReachedDistance)
                break;

            currentCornerIndex++;
        }

        if (Time.time - lastStuckCheckTime >= stuckCheckInterval)
        {
            Vector3 delta = transform.position - lastStuckCheckPos;
            delta.y = 0f;

            if (delta.magnitude < stuckMoveThreshold && HasPath)
            {
                TeleportToCurrentCorner();
            }

            lastStuckCheckPos = transform.position;
            lastStuckCheckTime = Time.time;
        }
    }
    
    private void TeleportToCurrentCorner()
    {
        if (!HasPath) return;

        Vector3 corner = corners[currentCornerIndex];
        
        NavMeshHit snap;
        if (NavMesh.SamplePosition(corner, out snap, navMeshSampleRadius, areaMask))
        {
            corner = snap.position;
        }

        if (cachedRigidbody != null)
        {
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
            cachedRigidbody.position = corner;
        }

        transform.position = corner;
        currentCornerIndex++;
    }

    public bool SetDestination(Vector3 worldDestination)
    {
        if (path == null) path = new NavMeshPath();

        if (hasDestination && (worldDestination - destination).sqrMagnitude
            < destinationMoveThreshold * destinationMoveThreshold)
        {
            if (Time.time - lastRepathTime < repathInterval)
            {
                return HasPath;
            }
        }

        Vector3 sampledStart;
        if (!TrySampleOnNavMesh(transform.position, out sampledStart))
        {
            ClearPath();
            return false;
        }

        Vector3 sampledEnd;
        if (!TrySampleOnNavMesh(worldDestination, out sampledEnd))
        {
            ClearPath();
            return false;
        }

        if (!NavMesh.CalculatePath(sampledStart, sampledEnd, areaMask, path))
        {
            ClearPath();
            return false;
        }

        corners = path.corners;
        currentCornerIndex = corners.Length > 1 ? 1 : 0; // skip the start corner
        destination = sampledEnd;
        hasDestination = true;
        lastRepathTime = Time.time;
        lastStuckCheckPos = transform.position;
        lastStuckCheckTime = Time.time;
        return HasPath;
    }

    public void ClearPath()
    {
        corners = System.Array.Empty<Vector3>();
        currentCornerIndex = 0;
        hasDestination = false;
        if (path != null) path.ClearCorners();
    }

    private bool TrySampleOnNavMesh(Vector3 worldPosition, out Vector3 sampled)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(worldPosition, out hit, navMeshSampleRadius, areaMask))
        {
            sampled = hit.position;
            return true;
        }

        sampled = worldPosition;
        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugPath || corners == null || corners.Length < 2) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            Gizmos.DrawLine(corners[i], corners[i + 1]);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(currentCornerPosition, cornerReachedDistance);

        if (hasDestination)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(destination, arrivedDistance);
        }
    }
#endif
}
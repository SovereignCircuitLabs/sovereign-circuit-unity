using System.Collections.Generic;
using UnityEngine;

public class WayPointNavigator : MonoBehaviour
{
    [SerializeField] private float wayPointReachedDistance = 1f; // How far is it to reach the path point
    [SerializeField] private bool debugPath = false;

    public List<WayPoint> currentPath;
    private int currentWayPointIndex = 0;
    public bool HasPath => currentPath != null && currentPath.Count > 0;
    public Vector3 currentWayPointPosition => HasPath ?
        currentPath[currentWayPointIndex].Position : transform.position;

    void Update()
    {
        if (!HasPath) return;

        float distanceToWayPoint = Vector3.Distance(transform.position, currentWayPointPosition);

        if (distanceToWayPoint <= wayPointReachedDistance)
        {
            currentWayPointIndex++;
            if (currentWayPointIndex >= currentPath.Count)
            {
                currentPath = null;
            }
        }
    }
    
    public void SetDestination(Vector3 destination)
    {
        var targetPoint = WayPointManager.Instance.GetNearestWayPoint(destination);
        var startPoint = WayPointManager.Instance.GetNearestWayPoint(transform.position);

        if (targetPoint != null && startPoint != null)
        {
            currentPath = WayPointManager.Instance.FindPathByAStar(startPoint, targetPoint);
            currentWayPointIndex = 0;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugPath || !HasPath) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.DrawLine(currentPath[i].Position, currentPath[i + 1].Position);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(currentWayPointPosition, wayPointReachedDistance);
    }
#endif
}
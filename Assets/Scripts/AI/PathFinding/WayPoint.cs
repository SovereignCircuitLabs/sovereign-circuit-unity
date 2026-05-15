using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WayPointConnection
{
    public WayPoint targetPoint;
    public float cost = 1;
}

public class WayPoint : MonoBehaviour
{
    [SerializeField] private List<WayPointConnection>
        connections =
            new List<WayPointConnection>();

    public float WayPointRadius = 15f;
    public LayerMask obstacleLayer;

    public Vector3 Position => transform.position;
    public List<WayPointConnection> Connections => connections;


    // Called when the data is modified
    void OnValidate()
    {
        if (connections != null)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] == null || connections[i].targetPoint == null || connections[i].targetPoint == this)
                {
                    connections.RemoveAt(i);
                }
            }
        }
    }

    public void AutomaticGenerateWaypoints(WayPoint[] allWayPoints)
    {
        connections.Clear();
        foreach (var point in allWayPoints)
        {
            if (point == this) continue;

            float distance = Vector3.Distance(Position, point.Position);

            if (distance <= WayPointRadius)
            {
                if (!Physics.Raycast(Position, point.Position - Position, distance, obstacleLayer))
                {
                    connections.Add(new WayPointConnection
                    {
                        targetPoint = point,
                        cost = distance
                    });
                }
            }
        }
    }

    public void AutomaticGenerateWaypoints(List<WayPoint> allWayPoints)
    {
        connections.Clear();
        foreach (var point in allWayPoints)
        {
            if (point == this) continue;

            //float distance = Vector3.Distance(Position, point.Position);
            float distance = WayPointManager.Instance.CalculateManhattanDistance(this, point);

            if (distance <= WayPointRadius)
            {
                connections.Add(new WayPointConnection
                {
                    targetPoint = point,
                    cost = distance
                });
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(Position, 0.5f); // myself

        if (connections != null)
        {
            foreach (var connection in connections)
            {
                if (connection.targetPoint != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(Position, connection.targetPoint.Position);
                }
            }
        }
    }
#endif
}
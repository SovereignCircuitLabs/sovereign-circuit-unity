using System.Collections.Generic;
using UnityEngine;

public class WayPointManager : Singleton<WayPointManager>
{
    private class PathNode
    {
        public WayPoint waypoint;
        public float gCost;
        public float hCost;
        public float fCost => gCost + hCost; // fCost = gCost + hCost

        public PathNode(WayPoint waypoint, float gCost, float hCost)
        {
            this.waypoint = waypoint;
            this.gCost = gCost;
            this.hCost = hCost;
        }
    }

    [SerializeField] List<WayPoint> waypoints = new List<WayPoint>();

    public List<WayPoint> GetWayPoints()
    {
        // if (waypoints.Count == 0)
        //     InitializeWaypoints();
        return waypoints;
    }

    void Start()
    {
        //InitializeWaypoints();
    }

    public void AddWayPoint(WayPoint wayPoint)
    {
        if (wayPoint != null && !waypoints.Contains(wayPoint))
        {
            waypoints.Add(wayPoint);
        }
    }

    public void InitializeCurrentWaypointsAdded()
    {
        foreach (var points in waypoints)
        {
            points.AutomaticGenerateWaypoints(waypoints);
        }
    }

    public void InitializeWaypoints()
    {
        waypoints.Clear();
        WayPoint[] wayPointsArr = FindObjectsOfType<WayPoint>();

        foreach (var points in wayPointsArr)
        {
            points.AutomaticGenerateWaypoints(wayPointsArr);
        }

        waypoints.AddRange(wayPointsArr);
    }

    public WayPoint GetNearestWayPoint(Vector3 position)
    {
        WayPoint nearestWaypoint = null;
        float minDistance = float.MaxValue;

        foreach (var point in waypoints)
        {
            float distance = Vector3.Distance(point.Position, position);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearestWaypoint = point;
            }
        }

        return nearestWaypoint;
    }

    public List<WayPoint> FindPathByAStar(WayPoint startPoint, WayPoint endPoint)
    {
        if (startPoint == null || endPoint == null) return new List<WayPoint>();

        var frontier = new List<PathNode>(); // Nodes to be visited
        var visited = new HashSet<WayPoint>();

        var cameFrom = new Dictionary<WayPoint, WayPoint>(); // Record the previous node of the path
        var costSoFar =
            new Dictionary<WayPoint, float>();

        //PathNode startNode = new PathNode(startPoint, 0, Vector3.Distance(startPoint.Position, endPoint.Position));
        PathNode startNode = new PathNode(startPoint, 0, CalculateManhattanDistance(startPoint, endPoint));
        frontier.Add(startNode);
        costSoFar[startNode.waypoint] = 0;

        while (frontier.Count > 0)
        {
            // Find the node with the smallest fCost
            PathNode current = frontier[0];
            int currentIndex = 0;
            for (int i = 0; i < frontier.Count; i++)
            {
                if (frontier[i].fCost < current.fCost)
                {
                    current = frontier[i];
                    currentIndex = i;
                }
            }

            frontier.RemoveAt(currentIndex);
            visited.Add(current.waypoint);

            if (current.waypoint == endPoint)
                break;

            foreach (var connection in current.waypoint.Connections)
            {
                var next = connection.targetPoint;
                if (visited.Contains(next)) continue;

                if (connection.cost > 15f)
                    continue;

                float newCost = costSoFar[current.waypoint] + connection.cost;

                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    //float hCost = Vector3.Distance(next.Position, endPoint.Position);
                    float hCost = CalculateManhattanDistance(next, endPoint);

                    PathNode nextNode = new PathNode(next, newCost, hCost);

                    frontier.Add(nextNode);

                    if (cameFrom.ContainsKey(next))
                    {
                        cameFrom[next] = current.waypoint;
                    }
                    else
                    {
                        cameFrom.Add(next, current.waypoint);
                    }
                }
            }
        }

        return ReconstructPathByCamFrom(cameFrom, startPoint, endPoint);
    }

    List<WayPoint> ReconstructPathByCamFrom(Dictionary<WayPoint, WayPoint> cameFrom, WayPoint startPoint,
        WayPoint endPoint)
    {
        List<WayPoint> path = new List<WayPoint>();
        WayPoint current = endPoint;

        while (current != startPoint)
        {
            path.Add(current);
            if (!cameFrom.ContainsKey(current))
            {
                Debug.LogWarning("No path found");
                return null;
            }

            current = cameFrom[current];
        }

        path.Add(startPoint);
        path.Reverse();

        return path;
    }

    public float CalculateManhattanDistance(WayPoint current, WayPoint target)
    {
        float distance = Mathf.Abs(current.Position.x - target.Position.x) +
                         Mathf.Abs(current.Position.z - target.Position.z);

        return distance;
    }
}
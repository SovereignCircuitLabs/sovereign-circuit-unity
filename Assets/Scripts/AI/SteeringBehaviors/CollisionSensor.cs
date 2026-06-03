using UnityEngine;

public class CollisionSensor : MonoBehaviour
{
    public float rayStart = 0.5f;
    public float rayHeight = 1f;
    public float rayLength = 10f;
    public int rayCount = 36;
    public LayerMask collisionLayers;

    public bool GetCollisionFreeDirection(Vector3 desiredDirection, out Vector3 outDirection)
    {
        desiredDirection.Normalize();
        outDirection = desiredDirection;

        if (desiredDirection == Vector3.zero) return false;

        Vector3 bestDirection = Vector3.zero;

        Vector3 bestDirection_right = GetBestDirectionHalf(1, desiredDirection);
        Vector3 bestDirection_left = GetBestDirectionHalf(-1, desiredDirection);

        if (Vector3.Dot(transform.forward, bestDirection_left) > Vector3.Dot(transform.forward, bestDirection_right))
        {
            bestDirection = bestDirection_left;
        }
        else
        {
            bestDirection = bestDirection_right;
        }

        if (bestDirection != desiredDirection)
        {
            outDirection = bestDirection;
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// sign == 1: Right half
    /// sign == -1: Left half
    /// </summary>
    Vector3 GetBestDirectionHalf(int sign, Vector3 desiredDirection)
    {
        Vector3 result = Vector3.zero;
        for (int i = 0; i < rayCount / 2; i++)
        {
            float angle = sign * (360f / rayCount) * i;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * desiredDirection;

            bool collision = false;
            RaycastHit hit;
            collision = Physics.Raycast(transform.position + direction * rayStart + new Vector3(0, rayHeight, 0),
                direction, out hit, rayLength,
                collisionLayers);

            if (collision)
            {
                Debug.DrawRay(transform.position + direction * rayStart + new Vector3(0, rayHeight, 0),
                    direction * hit.distance, Color.red);
            }
            else // No collision
            {
                Debug.DrawRay(transform.position + direction * rayStart + new Vector3(0, rayHeight, 0),
                    direction * rayLength, Color.green);
                result = direction;
                break;
            }
        }

        return result;
    }
}
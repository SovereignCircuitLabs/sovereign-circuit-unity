using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SteeringBehaviors : MonoBehaviour
{
    [Header("General")] public float maxVelocity = 3.5f;
    public float maxAcceleration = 10f;
    public float turnSpeed = 20f;

    // If the distance to the target point is less than this value, it is considered to reach the target point
    [Header("Arrive Behavior")] public float targetRadius = 0.005f;

    public float slowRadius = 1f; // Start deceleration within this radius
    public float timeToTarget = 0.1f;

    private Rigidbody rb;
    private Animator animator;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Move
    /// </summary>
    public void Steer(Vector3 linearAcceleration)
    {
        rb.velocity += linearAcceleration * Time.deltaTime;

        if (rb.velocity.magnitude > maxVelocity)
        {
            rb.velocity = rb.velocity.normalized * maxVelocity;
        }

        if (animator != null)
            animator.SetFloat("Speed", rb.velocity.magnitude);
    }

    public Vector3 Seek(Vector3 targetPosition, float maxSeekAcceleration)
    {
        Vector3 acceleration = targetPosition - transform.position;
        acceleration.Normalize();
        acceleration *= maxSeekAcceleration;
        return acceleration;
    }

    public Vector3 Seek(Vector3 targetPosition)
    {
        return Seek(targetPosition, maxAcceleration);
    }

    public bool IsArrived(Vector3 targetPosition)
    {
        //return (targetPosition - transform.position).sqrMagnitude <= targetRadius * targetRadius;
        Vector3 delta = targetPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= targetRadius * targetRadius;
    }

    public Vector3 Arrive(Vector3 targetPosition)
    {
        Vector3 targetVelocity = targetPosition - rb.position;
        targetVelocity.y = 0f;
        float distance = targetVelocity.magnitude;

        if (distance <= targetRadius)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return Vector3.zero;
        }

        float targetSpeed;
        if (distance > slowRadius)
        {
            targetSpeed = maxVelocity;
        }
        else // Start deceleration within slowRadius
        {
            targetSpeed = maxVelocity * (distance / slowRadius);
        }

        targetVelocity.Normalize();
        targetVelocity *= targetSpeed;
        
        Vector3 currentVelocity = rb.velocity;
        currentVelocity.y = 0f;

        Vector3 acceleration = targetVelocity - currentVelocity;
        acceleration *= 1 / timeToTarget;

        if (acceleration.magnitude > maxAcceleration)
        {
            acceleration.Normalize();
            acceleration *= maxAcceleration;
        }

        return acceleration;
    }

    /// <summary>
    /// Smooth turn
    /// </summary>
    public void LookMoveDirection()
    {
        Vector3 dir = rb.velocity;
        LookAtDirection(dir);
    }

    public void LookAtDirection(Vector3 direction)
    {
        direction.Normalize();
        if (direction.sqrMagnitude > 0.001f)
        {
            float toRotation = -1 * (Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg) + 90;
            float rotation = Mathf.LerpAngle(transform.rotation.eulerAngles.y, toRotation, Time.deltaTime * turnSpeed);
            transform.rotation = Quaternion.Euler(0, rotation, 0);
        }
    }
}
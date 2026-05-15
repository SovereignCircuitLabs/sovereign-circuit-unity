using System.Collections;
using UnityEngine;

// 巡逻行为
public class WanderBehaviors : MonoBehaviour
{
    public Vector2 targetChangeRange = new Vector2(2, 6);
    public float wanderRadius = 2f;
    public float targetHeight = 0.5f;

    [HideInInspector] public Vector3 targetPosition;
    SteeringBehaviors steeringBehaviors;
    Rigidbody rb;

    void Awake()
    {
        steeringBehaviors = GetComponent<SteeringBehaviors>();
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        StartCoroutine(targetPositionChange());
    }

    // Regularly change the target position
    IEnumerator targetPositionChange()
    {
        while (true)
        {
            float theta = Random.value * 2 * Mathf.PI;
            Vector3 wanderTarget = new Vector3(
                wanderRadius * Mathf.Cos(theta),
                0,
                wanderRadius * Mathf.Sin(theta));
            wanderTarget.Normalize();
            wanderTarget *= wanderRadius;

            targetPosition = transform.position + wanderTarget;
            targetPosition.y = targetHeight;

            yield return new WaitForSeconds(Random.Range(targetChangeRange.x, targetChangeRange.y));
        }
    }

    public Vector3 GetSteering()
    {
        Debug.DrawLine(transform.position, targetPosition, Color.gray);

        return steeringBehaviors.Seek(targetPosition);
    }
}
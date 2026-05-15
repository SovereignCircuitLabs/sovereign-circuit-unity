using UnityEngine;

[DisallowMultipleComponent]
public class FreeFlyCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float fastMoveMultiplier = 4f;
    [SerializeField] private float slowMoveMultiplier = 0.25f;
    [SerializeField] private float verticalSpeed = 6f;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 2.5f;
    [SerializeField] private bool requireRightMouseButton = true;
    [SerializeField] private bool lockCursorWhileLooking = true;

    private float yaw;
    private float pitch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToMainCamera()
    {
        Camera camera = Camera.main;
        if (camera == null || camera.GetComponent<FreeFlyCameraController>() != null)
        {
            return;
        }

        camera.gameObject.AddComponent<FreeFlyCameraController>();
    }

    private void Awake()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = NormalizePitch(euler.x);
    }

    private void Update()
    {
        UpdateLook();
        UpdateMovement();
    }

    private void UpdateLook()
    {
        bool canLook = !requireRightMouseButton || Input.GetMouseButton(1);
        if (!canLook)
        {
            ReleaseCursor();
            return;
        }

        if (lockCursorWhileLooking)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement()
    {
        Vector3 localMove = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) localMove += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) localMove += Vector3.back;
        if (Input.GetKey(KeyCode.D)) localMove += Vector3.right;
        if (Input.GetKey(KeyCode.A)) localMove += Vector3.left;

        Vector3 worldMove = transform.TransformDirection(localMove.normalized);

        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) worldMove += Vector3.up;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) worldMove += Vector3.down;

        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= fastMoveMultiplier;
        }
        else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            speed *= slowMoveMultiplier;
        }

        Vector3 horizontal = Vector3.ProjectOnPlane(worldMove, Vector3.up);
        Vector3 vertical = Vector3.Project(worldMove, Vector3.up);
        transform.position += (horizontal * speed + vertical * verticalSpeed) * Time.unscaledDeltaTime;
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

    private void ReleaseCursor()
    {
        if (!lockCursorWhileLooking || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}

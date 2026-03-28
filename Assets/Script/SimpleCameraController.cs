using UnityEngine;

public class SimpleCameraController : MonoBehaviour
{
    [Header("Move")]
    public float moveSpeed = 10f;
    public float fastMoveMultiplier = 3f;
    public float verticalMoveSpeed = 8f;

    [Header("Look")]
    public float lookSensitivity = 2f;
    public float minPitch = -80f;
    public float maxPitch = 80f;
    public bool requireRightMouseButton = true;

    private float _yaw;
    private float _pitch;

    private void Awake()
    {
        Vector3 euler = transform.rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = NormalizePitch(euler.x);
    }

    private void Update()
    {
        HandleLook();
        HandleMove();
    }

    private void HandleLook()
    {
        if (requireRightMouseButton && !Input.GetMouseButton(1))
        {
            return;
        }

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        _yaw += mouseX * lookSensitivity;
        _pitch -= mouseY * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void HandleMove()
    {
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= fastMoveMultiplier;
        }

        float forward = Input.GetAxisRaw("Vertical");
        float right = Input.GetAxisRaw("Horizontal");
        float vertical = 0f;

        if (Input.GetKey(KeyCode.E))
        {
            vertical += 1f;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            vertical -= 1f;
        }

        Vector3 move =
            transform.forward * forward * speed +
            transform.right * right * speed +
            Vector3.up * vertical * verticalMoveSpeed;

        transform.position += move * Time.deltaTime;
    }

    private static float NormalizePitch(float pitch)
    {
        if (pitch > 180f)
        {
            pitch -= 360f;
        }

        return pitch;
    }
}

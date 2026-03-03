using UnityEngine;

public class FreeCamera : MonoBehaviour
{
    // Note: Adapted from UnityEngine.Rendering.FreeCamera

    /// <summary>
    /// Movement Speed.
    /// </summary>
    public float m_MoveSpeed = 10.0f;

    /// <summary>
    /// Rotation speed when using the mouse.
    /// </summary>
    public float m_MouseSensitivity = 4.0f;

    const string k_MouseX = "Mouse X";
    const string k_MouseY = "Mouse Y";

    const string k_Vertical = "Vertical";
    const string k_Horizontal = "Horizontal";

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }

    float inputRotateAxisX, inputRotateAxisY;
    float inputVertical, inputHorizontal, inputYAxis;
    bool boostHeld;

    void UpdateInputs()
    {
        inputRotateAxisX = Input.GetAxis(k_MouseX) * m_MouseSensitivity;
        inputRotateAxisY = Input.GetAxis(k_MouseY) * m_MouseSensitivity;

        inputVertical = Input.GetAxis(k_Vertical);
        inputHorizontal = Input.GetAxis(k_Horizontal);

        inputYAxis = Input.GetKey(KeyCode.Space) ? 1 : 0;
        inputYAxis = Input.GetKey(KeyCode.LeftControl) ? -1 : inputYAxis;

        boostHeld = Input.GetKey(KeyCode.LeftShift);
    }

    void HandleMove()
    {
        float moveSpeed = Time.deltaTime * m_MoveSpeed;
        if (boostHeld)
            moveSpeed *= 2.0f;

        transform.position += transform.forward * (moveSpeed * inputVertical)
                           + transform.right * (moveSpeed * inputHorizontal)
                           + Vector3.up * (moveSpeed * inputYAxis);
    }

    void HandleLook()
    {
        bool mouseMoved = inputRotateAxisX != 0.0f || inputRotateAxisY != 0.0f;
        if (mouseMoved)
        {
            float rotationX = transform.localEulerAngles.x;
            float newRotationY = transform.localEulerAngles.y + inputRotateAxisX;

            // Weird clamping code due to weird Euler angle mapping...
            float newRotationX = (rotationX - inputRotateAxisY);
            if (rotationX <= 90.0f && newRotationX >= 0.0f)
                newRotationX = Mathf.Clamp(newRotationX, 0.0f, 90.0f);
            if (rotationX >= 270.0f)
                newRotationX = Mathf.Clamp(newRotationX, 270.0f, 360.0f);

            transform.localRotation = Quaternion.Euler(newRotationX, newRotationY, transform.localEulerAngles.z);
        }
    }

    void Update()
    {
        UpdateInputs();
        HandleMove();
        HandleLook();
    }
}

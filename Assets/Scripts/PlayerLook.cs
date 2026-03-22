using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform pitchTarget;
    [SerializeField] private InputActionReference lookAction;

    [Header("Look")]
    [SerializeField, Range(0.01f, 1f)] private float sensitivity = 0.11f;
    [SerializeField] private float minPitch = -88f;
    [SerializeField] private float maxPitch = 88f;

    private float pitch;

    private void OnEnable()
    {
        if (lookAction != null)
        {
            lookAction.action.Enable();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        if (lookAction != null)
        {
            lookAction.action.Disable();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (lookAction == null || pitchTarget == null)
        {
            return;
        }

        Vector2 look = lookAction.action.ReadValue<Vector2>();

        float mouseX = look.x * sensitivity;
        float mouseY = look.y * sensitivity;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        pitchTarget.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}

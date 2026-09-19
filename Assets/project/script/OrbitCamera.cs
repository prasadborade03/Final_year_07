using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 10f;
    public float rotationSpeed = 5f;

    float yaw = 0f;
    float pitch = 20f;

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * rotationSpeed;
            pitch -= Input.GetAxis("Mouse Y") * rotationSpeed;
        }

        pitch = Mathf.Clamp(pitch, -20f, 80f);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);

        transform.position = target.position - (rotation * Vector3.forward * distance);
        transform.LookAt(target);
    }
}
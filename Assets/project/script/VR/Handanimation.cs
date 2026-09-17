using UnityEngine;
using UnityEngine.InputSystem;

public class HandAnimation : MonoBehaviour
{
    [SerializeField] private InputActionReference gripActionRefrerence;
    [SerializeField] private InputActionReference triggerActionRefrerence;

    private Animator animator;

    [SerializeField] private string gripActionName = "Grip";
    [SerializeField] private string triggerActionName = "Trigger";

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Animator component not found on the GameObject: " + gameObject.name);
        }
    }

    void Update()
    {
        if (animator == null) return;

        if (gripActionRefrerence != null && gripActionRefrerence.action != null)
        {
            float gripValue = gripActionRefrerence.action.ReadValue<float>();
            animator.SetFloat(gripActionName, gripValue);
        }

        if (triggerActionRefrerence != null && triggerActionRefrerence.action != null)
        {
            float triggerValue = triggerActionRefrerence.action.ReadValue<float>();
            animator.SetFloat(triggerActionName, triggerValue);
        }
    }
}
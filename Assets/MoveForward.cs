using UnityEngine;

public class MoveForward : MonoBehaviour
{
    [SerializeField] float m_Speed = 10.0f;

    void Update()
    {
        transform.position += Time.deltaTime * m_Speed * transform.forward;
    }
}

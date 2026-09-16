using UnityEngine;

namespace DeNelle.Village.World.Camps
{
    [DisallowMultipleComponent]
    public sealed class OwnedTemplateIdentity : MonoBehaviour
    {
        [SerializeField] private string _templateId;
        public string TemplateId => _templateId;
    }
}

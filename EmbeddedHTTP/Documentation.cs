using System;

namespace EmbeddedHTTP
{
    /// <summary>
    /// A representation of runtime documentation.
    /// </summary>
    public class Documentation
    {
        /// <summary>
        /// The endpoint this documentation is associated with.
        /// </summary>
        [Description("The endpoint this documentation is associated with.")]
        public string Endpoint { get; set; } = null;
        /// <summary>
        /// The description of the endpoint.
        /// </summary>
        [Description("The description of the endpoint.")]
        public string Description { get; set; } = null;
        /// <summary>
        /// Documentation about the request format.
        /// </summary>
        [Children]
        [Description("Documentation about the request format.")]
        public JsonDoc[] Request { get; set; } = new JsonDoc[0];
        /// <summary>
        /// Documentation about the response format.
        /// </summary>
        [Children]
        [Description("Documentation about the response format.")]
        public JsonDoc[] Response { get; set; } = new JsonDoc[0];

        /// <summary>
        /// A representation of a documented field. Compatible with JSON format.
        /// </summary>
        public class JsonDoc
        {
            /// <summary>
            /// The name of the field.
            /// </summary>
            [Description("The name of the field.")]
            public string Field { get; set; } = null;
            /// <summary>
            /// The data type of the field.
            /// </summary>
            [Description("The data type of the field.")]
            public string Type { get; set; } = typeof(void).Name;
            /// <summary>
            /// The description of the field.
            /// </summary>
            [Description("The description of the field.")]
            public string Description { get; set; } = null;
            /// <summary>
            /// The children fields of the field type.
            /// </summary>
            [Description("The children fields of the field type.")]
            public JsonDoc[] Children { get; set; } = new JsonDoc[0];
        }

        /// <summary>
        /// An attribute that contains documentation text for the target.
        /// </summary>
        [AttributeUsage(AttributeTargets.All)]
        public class DescriptionAttribute : Attribute
        {
            /// <summary>
            /// The documentation text for the target.
            /// </summary>
            public string Value { get; set; }

            public DescriptionAttribute(string description)
            {
                Value = description;
            }
        }

        /// <summary>
        /// An attribute to indicate that a property has additional sub-fields that need documenting.
        /// </summary>
        [AttributeUsage(AttributeTargets.Property)]
        public class ChildrenAttribute : Attribute { }
    }
}

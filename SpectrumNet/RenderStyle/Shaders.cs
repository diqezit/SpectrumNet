namespace SpectrumNet
{
    /// <summary>
    /// Place for all shaders for renders with improved lighting, performance, and features.
    /// </summary>
    public class Shaders
    {
        public static readonly string vertex3DShader = @"
            #version 400 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            layout(location = 2) in vec2 aTexCoord;
            
            uniform mat4 projection;
            uniform mat4 modelview;
            uniform mat4 model;
            
            out vec3 Normal;
            out vec3 FragPos;
            out vec3 WorldPos;
            out vec2 TexCoord;
            
            void main()
            {
                gl_Position = projection * modelview * vec4(aPosition, 1.0);
                FragPos = vec3(modelview * vec4(aPosition, 1.0));
                WorldPos = vec3(model * vec4(aPosition, 1.0));
                Normal = mat3(transpose(inverse(modelview))) * aNormal;
                TexCoord = aTexCoord;
            }
        ";

        public static readonly string fragment3DShader = @"
            #version 400 core
            
            struct Material {
                vec3 ambient;
                vec3 diffuse;
                vec3 specular;
                float shininess;
                float opacity;
            };
            
            struct Light {
                vec3 position;
                vec3 color;
                float intensity;
                float attenuation;
            };
            
            in vec3 Normal;
            in vec3 FragPos;
            in vec3 WorldPos;
            in vec2 TexCoord;
            
            uniform vec4 color;
            uniform Light lights[4];
            uniform int numLights;
            uniform vec3 viewPos;
            uniform Material material;
            uniform bool useTexture;
            uniform sampler2D diffuseTexture;
            uniform bool receiveShadows;
            
            out vec4 FragColor;
            
            vec3 calculateLight(Light light, vec3 normal, vec3 viewDir, Material mat)
            {
                // Ambient
                vec3 ambient = mat.ambient * light.color * light.intensity;
                
                // Diffuse
                vec3 lightDir = normalize(light.position - FragPos);
                float diff = max(dot(normal, lightDir), 0.0);
                vec3 diffuse = diff * mat.diffuse * light.color * light.intensity;
                
                // Specular
                vec3 reflectDir = reflect(-lightDir, normal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), mat.shininess);
                vec3 specular = spec * mat.specular * light.color * light.intensity;
                
                // Attenuation
                float distance = length(light.position - FragPos);
                float attenuation = 1.0 / (1.0 + light.attenuation * distance + 
                                    light.attenuation * distance * distance);
                
                // Combine
                ambient *= attenuation;
                diffuse *= attenuation;
                specular *= attenuation;
                
                return ambient + diffuse + specular;
            }
            
            void main()
            {
                Material mat;
                vec3 objectColor;
                
                if (useTexture) {
                    vec4 texColor = texture(diffuseTexture, TexCoord);
                    objectColor = texColor.rgb;
                    mat.opacity = texColor.a * color.a;
                } else {
                    objectColor = color.rgb;
                    mat.opacity = color.a;
                }
                
                mat.ambient = objectColor * 0.3;
                mat.diffuse = objectColor;
                mat.specular = vec3(0.5);
                mat.shininess = 32.0;
                
                if (material.shininess > 0.0) {
                    mat = material;
                }
                
                vec3 normal = normalize(Normal);
                vec3 viewDir = normalize(viewPos - FragPos);
                vec3 result = objectColor * 0.1;
                
                for (int i = 0; i < numLights && i < 4; i++) {
                    result += calculateLight(lights[i], normal, viewDir, mat);
                }
                
                result = pow(result, vec3(1.0/2.2));
                FragColor = vec4(result, mat.opacity);
            }
        ";

        public static readonly string vertexShader = @"
            #version 400 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec2 aTexCoord;
            
            uniform mat4 projection;
            uniform mat4 modelview;
            
            out vec2 TexCoord;
            
            void main()
            {
                gl_Position = projection * modelview * vec4(aPosition, 1.0);
                TexCoord = aTexCoord;
            }
        ";

        public static readonly string fragmentShader = @"
            #version 400 core
            in vec2 TexCoord;
            
            uniform vec4 color;
            uniform bool useTexture;
            uniform sampler2D diffuseTexture;
            
            out vec4 FragColor;
            
            void main()
            {
                FragColor = useTexture 
                    ? texture(diffuseTexture, TexCoord) * color 
                    : color;
            }
        ";

        public static readonly string glowFragmentShader = @"
            #version 400 core
            in vec2 TexCoord;
            
            uniform vec4 color;
            uniform float time;
            uniform float intensity;
            uniform float pulseRate;
            uniform bool useTexture;
            uniform sampler2D diffuseTexture;
            
            out vec4 FragColor;
            
            void main()
            {
                vec4 baseColor = useTexture 
                    ? texture(diffuseTexture, TexCoord) * color 
                    : color;
                
                float glowFactor = 0.5 + 0.5 * sin(time * pulseRate);
                glowFactor = mix(1.0, glowFactor, intensity);
                vec3 glowColor = baseColor.rgb * glowFactor * (1.0 + intensity);
                
                FragColor = vec4(glowColor, baseColor.a);
            }
        ";

        public static readonly string postProcessVertexShader = @"
            #version 400 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec2 aTexCoord;
            
            out vec2 TexCoord;
            
            void main()
            {
                gl_Position = vec4(aPosition, 1.0);
                TexCoord = aTexCoord;
            }
        ";

        public static readonly string bloomFragmentShader = @"
    #version 400 core
    in vec2 TexCoord;
    
    uniform sampler2D screenTexture;
    uniform float bloomThreshold;
    uniform float bloomIntensity;
    
    out vec4 FragColor;
    
    void main()
    {
        vec4 color = texture(screenTexture, TexCoord);
        float brightness = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
        FragColor = brightness > bloomThreshold 
            ? color * bloomIntensity 
            : vec4(0.0, 0.0, 0.0, color.a);
    }
";

        public static readonly string shadowMapVertexShader = @"
            #version 400 core
            layout(location = 0) in vec3 aPosition;
            
            uniform mat4 lightSpaceMatrix;
            uniform mat4 model;
            
            void main()
            {
                gl_Position = lightSpaceMatrix * model * vec4(aPosition, 1.0);
            }
        ";

        public static readonly string shadowMapFragmentShader = @"
            #version 400 core
            
            void main()
            {
                // gl_FragDepth = gl_FragCoord.z;
            }
        ";

        public static readonly string vertexSceneShader = @"
    #version 330 core
    layout(location = 0) in vec3 aPosition;
    layout(location = 2) in vec3 aColor;
    
    uniform mat4 projection;
    uniform mat4 modelview;
    
    out vec3 outColor;
    
    void main()
    {
        gl_Position = projection * modelview * vec4(aPosition, 1.0);
        outColor = aColor;
    }
";

        public static readonly string fragmentSceneShader = @"
    #version 330 core
    
    in vec3 outColor;
    out vec4 FragColor;
    
    void main()
    {
        FragColor = vec4(outColor, 1.0);
    }
";

    }
}
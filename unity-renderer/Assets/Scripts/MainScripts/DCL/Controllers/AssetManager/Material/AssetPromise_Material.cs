﻿using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DCL.Helpers;
using UnityEngine;

namespace DCL
{
    public class AssetPromise_Material : AssetPromise<Asset_Material>
    {
        private const string MATERIAL_RESOURCES_PATH = "Materials/";
        private const string PBR_MATERIAL_NAME = "ShapeMaterial";

        private MaterialModel model;

        private AssetPromise_TextureResource emissionAssetPromiseTextureResource = null;
        private AssetPromise_TextureResource alphaAssetPromiseTextureResource = null;
        private AssetPromise_TextureResource baseAssetPromiseTextureResource = null;
        private AssetPromise_TextureResource bumpPromiseTextureResource = null;

        private Coroutine loadCoroutine;

        public AssetPromise_Material(MaterialModel model) { this.model = model; }

        protected override void OnAfterLoadOrReuse() {  }

        protected override void OnBeforeLoadOrReuse() {  }

        protected override void OnCancelLoading()
        {
            CleanPromises();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            CleanPromises();
        }

        private void CleanPromises()
        {
            if (emissionAssetPromiseTextureResource != null)
                AssetPromiseKeeper_TextureResource.i.Forget(emissionAssetPromiseTextureResource);
            if (alphaAssetPromiseTextureResource != null)
                AssetPromiseKeeper_TextureResource.i.Forget(alphaAssetPromiseTextureResource);
            if (baseAssetPromiseTextureResource != null)
                AssetPromiseKeeper_TextureResource.i.Forget(baseAssetPromiseTextureResource);
            if (bumpPromiseTextureResource != null)
                AssetPromiseKeeper_TextureResource.i.Forget(bumpPromiseTextureResource);
            
            CoroutineStarter.Stop(loadCoroutine);
            loadCoroutine = null;
        }

        protected override void OnLoad(Action OnSuccess, Action<Exception> OnFail)
        {
            CoroutineStarter.Stop(loadCoroutine);
            loadCoroutine = CoroutineStarter.Start(CreateMaterial(model, OnSuccess, OnFail));
        }
        
        public override object GetId() { return model; }

        private IEnumerator CreateMaterial(MaterialModel model, Action OnSuccess, Action<Exception> OnFail)
        {
            Material material = new Material(Utils.EnsureResourcesMaterial(MATERIAL_RESOURCES_PATH + PBR_MATERIAL_NAME));
#if UNITY_EDITOR
            material.name = "PBRMaterial_" + model.GetHashCode();
#endif
            material.SetColor(ShaderUtils.BaseColor, model.albedoColor);

            if (model.emissiveColor != Color.clear && model.emissiveColor != Color.black)
            {
                material.EnableKeyword("_EMISSION");
            }

            // METALLIC/SPECULAR CONFIGURATIONS
            material.SetColor(ShaderUtils.EmissionColor, model.emissiveColor * model.emissiveIntensity);
            material.SetColor(ShaderUtils.SpecColor, model.reflectivityColor);

            material.SetFloat(ShaderUtils.Metallic, model.metallic);
            material.SetFloat(ShaderUtils.Smoothness, 1 - model.roughness);
            material.SetFloat(ShaderUtils.EnvironmentReflections, model.microSurface);
            material.SetFloat(ShaderUtils.SpecularHighlights, model.specularIntensity * model.directIntensity);


            if (model.emissiveTexture != null)
                emissionAssetPromiseTextureResource = AssignTextureToMaterial(material, ShaderUtils.EmissionMap, model.emissiveTexture, OnFail);

            SetupTransparencyMode(material, model);

            if (model.alphaTexture != null)
                alphaAssetPromiseTextureResource = AssignTextureToMaterial(material, ShaderUtils.AlphaTexture, model.alphaTexture, OnFail);

            if (model.albedoTexture != null)
                baseAssetPromiseTextureResource = AssignTextureToMaterial(material, ShaderUtils.BaseMap, model.albedoTexture, OnFail);

            if (model.bumpTexture != null)
                bumpPromiseTextureResource = AssignTextureToMaterial(material, ShaderUtils.BumpMap, model.bumpTexture, OnFail);

            // Checked two times so they can be loaded at the same time
            if (model.alphaTexture != null)
                yield return  alphaAssetPromiseTextureResource;
            if (model.albedoTexture != null)
                yield return  baseAssetPromiseTextureResource;
            if (model.bumpTexture != null)
                yield return  bumpPromiseTextureResource;
            if (model.emissiveTexture != null)
                yield return  emissionAssetPromiseTextureResource;

            SRPBatchingHelper.OptimizeMaterial(material);
            asset.material = material;
            OnSuccess?.Invoke();
        }

        private void SetupTransparencyMode(Material material, MaterialModel model)
        {
            // Reset shader keywords
            material.DisableKeyword("_ALPHATEST_ON"); // Cut Out Transparency
            material.DisableKeyword("_ALPHABLEND_ON"); // Fade Transparency
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); // Transparent

            MaterialModel.TransparencyMode transparencyMode = (MaterialModel.TransparencyMode) model.transparencyMode;

            if (transparencyMode == MaterialModel.TransparencyMode.AUTO)
            {
                if (model.alphaTexture != null || model.albedoColor.a < 1f) //AlphaBlend
                {
                    transparencyMode = MaterialModel.TransparencyMode.ALPHA_BLEND;
                }
                else // Opaque
                {
                    transparencyMode = MaterialModel.TransparencyMode.OPAQUE;
                }
            }

            switch (transparencyMode)
            {
                case MaterialModel.TransparencyMode.OPAQUE:
                    material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Geometry;
                    material.SetFloat(ShaderUtils.AlphaClip, 0);
                    break;
                case MaterialModel.TransparencyMode.ALPHA_TEST: // ALPHATEST
                    material.EnableKeyword("_ALPHATEST_ON");

                    material.SetInt(ShaderUtils.SrcBlend, (int) UnityEngine.Rendering.BlendMode.One);
                    material.SetInt(ShaderUtils.DstBlend, (int) UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt(ShaderUtils.ZWrite, 1);
                    material.SetFloat(ShaderUtils.AlphaClip, 1);
                    material.SetFloat(ShaderUtils.Cutoff, model.alphaTest);
                    material.SetInt("_Surface", 0);
                    material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;
                case MaterialModel.TransparencyMode.ALPHA_BLEND: // ALPHABLEND
                    material.EnableKeyword("_ALPHABLEND_ON");

                    material.SetInt(ShaderUtils.SrcBlend, (int) UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt(ShaderUtils.DstBlend, (int) UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt(ShaderUtils.ZWrite, 0);
                    material.SetFloat(ShaderUtils.AlphaClip, 0);
                    material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetInt("_Surface", 1);
                    break;
                case MaterialModel.TransparencyMode.ALPHA_TEST_AND_BLEND:
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");

                    material.SetInt(ShaderUtils.SrcBlend, (int) UnityEngine.Rendering.BlendMode.One);
                    material.SetInt(ShaderUtils.DstBlend, (int) UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt(ShaderUtils.ZWrite, 0);
                    material.SetFloat(ShaderUtils.AlphaClip, 1);
                    material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetInt("_Surface", 1);
                    break;
            }
        }

        private AssetPromise_TextureResource AssignTextureToMaterial(Material material, int materialProperty, TextureModel model, Action<Exception> onFail)
        {
            AssetPromise_TextureResource promiseTextureResource = new AssetPromise_TextureResource(model);
            if (model != null)
            {
                promiseTextureResource.OnSuccessEvent += (x) =>
                {
                    SetMaterialTexture(material, materialProperty, x.texture2D);
                };
                promiseTextureResource.OnFailEvent += (x, error) =>
                {
                    onFail?.Invoke(error);
                };

                AssetPromiseKeeper_TextureResource.i.Keep(promiseTextureResource);
            }
            else
            {
                SetMaterialTexture(material, materialProperty, null);
            }
            return promiseTextureResource;
        }

        private void SetMaterialTexture(Material material, int materialPropertyId, Texture2D texture2D) { material.SetTexture(materialPropertyId, texture2D); }
    }
}
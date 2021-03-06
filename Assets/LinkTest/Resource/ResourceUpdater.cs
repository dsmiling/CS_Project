﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Xml;
using System;
#region
//@Author:lgr Date: 20150415 
#endregion
public class ResourcesUpdater : MonoBehaviour
{
    /// <summary>
    /// 本地资源根目录路径
    /// </summary>
    private string m_LocalResRootPath = null;

    /// <summary>
    /// 流媒体资源路径
    /// </summary>
    private string m_StreamResRootPath = null;

    /// <summary>
    /// 资源服务器根目录路径
    /// </summary>
    private string m_ServerResRootPath = null;

    /// <summary>
    /// 资源列表名称
    /// </summary>
    public const string RVL = "ResVersion.xml";
    /// <summary>
    /// 当前更新大小(KB)
    /// </summary>
    private int m_CurrentUpdateSize = 0;

    /// <summary>
    /// 已更新大小
    /// </summary>
    private int m_UpdatedSize = 0;

    /// <summary>
    /// 已更新资源数量
    /// </summary>
    private int m_UpdatedResNumber = 0;

    /// <summary>
    /// 更新资源总数量
    /// </summary>
    private int m_UpdateTotalNumber = 0;

    /// <summary>
    /// 本地资源版本号
    /// </summary>
    private string m_LocalVersion = "";

    /// <summary>
    /// 服务器资源版本号
    /// </summary>
    private string m_ServerVersion = "";

    /// <summary>
    /// 本地资源版本列表数据
    /// </summary>
    private Dictionary<string, VersionAssetData> m_LocalRVLDic = null;


    /// <summary>
    /// 资源版本列表数据
    /// </summary>
    private Dictionary<string, VersionAssetData> m_ServerRVLDic = null;

    /// <summary>
    /// 资源更新数据列表
    /// </summary>
    private List<VersionAssetData> m_UpdateResList = null;

    /// <summary>
    /// 是否在移动资源
    /// </summary>
    private bool m_IsMovingRes = false;

    /// <summary>
    /// 正在移动资源(新版不需要了)
    /// </summary>
    public event UpdateStateDelegate OnMovingResEvent;
    /// <summary>
    /// 资源移动完成
    /// </summary>
    public event UpdateStateDelegate MoveResFinishedEvent;

    /// <summary>
    /// 检查更新事件结果
    /// </summary>
    public event CheckUpdateDelegate CheckUpdateEvent;

    /// <summary>
    /// 更新错误事件
    /// </summary>
    public event UpdateErrorDelegate UpdateErrorEvent;

    /// <summary>
    /// 更新中事件（更新进度刷新）
    /// </summary>
    public event UpdateStateDelegate OnUpdatingEvent;

    public string Version { get { return m_LocalVersion; } }
    public int UpdatedSize { get { return m_UpdatedSize; } }
    public int UpdateTotalSize { get { return m_CurrentUpdateSize; } }
    public int UpdateTotalNumber { get { return m_UpdateTotalNumber; } }
    public int UpdatedNumber { get { return m_UpdatedResNumber; } }

    void Awake()
    {
        m_StreamResRootPath = "File:///" + Application.dataPath + "/StreamingAssets";
        m_LocalResRootPath = "File://" + Application.persistentDataPath;//windows下需要file 其他不需要
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        m_StreamResRootPath = "File:///" + Application.dataPath + "/StreamingAssets";
        //m_LocalResRootPath = Application.dataPath + "/../../AssetBundle";
        m_LocalResRootPath = Application.dataPath + "/../../AssetBundle/Local";
#elif UNITY_IPHONE
        m_StreamResRootPath = "File:///" + Application.dataPath + "/Raw";
        m_LocalResRootPath = Application.persistentDataPath;
#elif UNITY_ANDROID
        m_StreamResRootPath = "jar:File:///" + Application.dataPath + "!/assets";
        m_LocalResRootPath = Application.persistentDataPath;
#endif
    }

    /// <summary>
    /// 设置资源服务地址
    /// </summary>
    /// <param name="url"></param>
    public void SetResServerUrl(string url)
    {
        Debug.LogError("SetResServerUrl:"+url);
        m_ServerResRootPath = url;
    }

    /// <summary>
    /// 准备资源
    /// 为之后资源更新做准备
    /// </summary>
    public void PrepareResource()
    {
        m_UpdatedSize = 0;
        m_CurrentUpdateSize = 0;
        m_UpdatedResNumber = 0;
        m_UpdateTotalNumber = 0;
        
        StartCoroutine(MoveStreamingAssets());
    }
    /// <summary>
    /// 检查资源更新
    /// </summary>
    public void CheckUpdater()
    {
        if (m_ServerResRootPath == null)
        {
            Debug.LogError("Resources Server Url is null...");
            return;
        }

        m_UpdatedSize = 0;
        m_CurrentUpdateSize = 0;
        m_UpdatedResNumber = 0;
        m_UpdateTotalNumber = 0;
        StartCoroutine(OnUpdateVersionRes());
    }


    /// <summary>
    /// 开始更新
    /// </summary>
    public void StartUpdate()
    {
        if (m_UpdateResList.Count > 0)
        {
            StartCoroutine(UpdateNewRes(m_UpdateResList));
        }
    }

    /// <summary>
    /// 移动流媒体资源
    /// </summary>
    /// <returns></returns>
    private IEnumerator MoveStreamingAssets()
    {
        if (m_LocalRVLDic == null)
        {
            yield return StartCoroutine(ParserLocalRVL());
        }
        ///>获取流媒体文件夹内的资源列表
        string path = m_StreamResRootPath + "/" + RVL;
        WWW streamWWW = new WWW(ComTool.FormatTickUrl(path));
        yield return streamWWW;
        if (streamWWW.error == null)
        {
            m_ServerRVLDic = ParserResVersionList(streamWWW.text, ref m_ServerVersion);
            if (CompareVersion(m_LocalVersion, m_ServerVersion))
            {
                MoveResFinished();
            }
            else
            {
                string localVersionPath = m_LocalResRootPath + "/" + RVL;//local资源版本列表配置
                if (File.Exists(localVersionPath))
                {
                    MoveResFinished();
                }
                else {
                    try
                    {
                        FileInfo t = new FileInfo(localVersionPath);
                        
                        if (!t.Exists)
                        {
                            Directory.CreateDirectory(t.DirectoryName);
                        }
                        Debug.LogError("write resversion:" + localVersionPath);
                        File.WriteAllBytes(localVersionPath, streamWWW.bytes);
                        
                        //xml.Save 在实际真机环境中，如果不存在文件，将会报IO错误
                        /*
                        XmlDocument xml = new XmlDocument();
                        xml.LoadXml(streamWWW.text);
                        xml.Save(localVersionPath);
                         * */
                        MoveResFinished();
                    }catch(Exception e){
                        LogHelp.LogFile(e.ToString());
                    }
                }
                #region
                /*
                yield return StartCoroutine(CompareWithRVL());
                ///>是否需要移动资源
                if (m_UpdateResList != null && m_UpdateResList.Count > 0)
                {
                    m_IsMovingRes = true;
                    StartCoroutine(UpdateNewRes(m_UpdateResList));
                }
                else
                {
                    MoveResFinished();
                }
                * */
                #endregion
            }
            streamWWW.Dispose();
        }
        else
        {
            MoveResFinished();
        }
    }

    /// <summary>
    /// 解析本地列表
    /// </summary>
    /// <returns></returns>
    private IEnumerator ParserLocalRVL()
    {
        ///>获取本地资源版本数据
        string localPath = m_LocalResRootPath + "/" + RVL;
        if (File.Exists(localPath))
        {
            WWW localWWW = new WWW("File://" + localPath);
            //WWW localWWW = new WWW(localPath);
            yield return localWWW;
            if (localWWW.error != null)
            {
                Debug.LogError("ParserLocalRVL Error:" + localPath);
                Debug.LogError(localWWW.error);
                SendErrorCode(101);
            }
            else
            {
                Debug.LogError("ParserLocalRVL Success:" + localPath);
                m_LocalRVLDic = ParserResVersionList(localWWW.text, ref m_LocalVersion);
            }
            localWWW.Dispose();
        }
        else {
            Debug.LogError("file not exist:" + localPath);
        }
    }

    /// <summary>
    /// 更新版本资源
    /// </summary>
    /// <returns></returns>
    private IEnumerator OnUpdateVersionRes()
    {
        if (m_LocalRVLDic == null)
        {
            yield return StartCoroutine(ParserLocalRVL());
        }
        ///>获取服务器资源版本数据
        string path = m_ServerResRootPath + "/" + RVL;
        WWW serverWWW = new WWW(ComTool.FormatTickUrl(path));
        yield return serverWWW;
        if (serverWWW.error != null)
        {
            SendErrorCode(100);
        }
        m_ServerRVLDic = ParserResVersionList(serverWWW.text, ref m_ServerVersion);
        serverWWW.Dispose();
        yield return StartCoroutine(CompareWithRVL());
        ///>是否需要更新资源
        if (m_UpdateResList != null && m_UpdateResList.Count > 0)
        {
            if (CheckUpdateEvent != null)
            {
                CheckUpdateEvent(true);
            }
        }
        else
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// 比较资源列表
    /// </summary>
    /// <returns></returns>
    private IEnumerator CompareWithRVL()
    {
        ///>删除旧版本无用资源
        if (m_LocalRVLDic != null)
        {
            List<string> deleteResList = new List<string>();
            foreach (string key in m_LocalRVLDic.Keys)
            {
                if (!m_ServerRVLDic.ContainsKey(key))
                {
                    deleteResList.Add(key);
                }
            }

            yield return StartCoroutine(DeleteLowVersionRes(deleteResList));
        }

        ///>获取更新资源列表
        m_UpdateResList = new List<VersionAssetData>();
        foreach (string key in m_ServerRVLDic.Keys)
        {
            if (m_LocalRVLDic != null && m_LocalRVLDic.ContainsKey(key))
            {
                if (m_LocalRVLDic[key].md5 != m_ServerRVLDic[key].md5)
                {
                    m_UpdateResList.Add(m_ServerRVLDic[key]);
                    m_CurrentUpdateSize += m_ServerRVLDic[key].size;
                }
            }
            else
            {
                m_UpdateResList.Add(m_ServerRVLDic[key]);
                m_CurrentUpdateSize += m_ServerRVLDic[key].size;
            }
        }
        m_UpdateTotalNumber = m_UpdateResList.Count;
    }

    /// <summary>
    /// 删除旧版本资源
    /// </summary>
    /// <param name="list"></param>
    /// <returns></returns>
    private IEnumerator DeleteLowVersionRes(List<string> list)
    {
        if (list != null && list.Count > 0)
        {
            Debug.Log("Delete low version resources...");
            string localPath = "";
            foreach (string name in list)
            {
                localPath = GetAssetLocalPath(m_LocalRVLDic[name].path,m_LocalRVLDic[name].md5);
                DeleteAsset(localPath);
                m_LocalRVLDic.Remove(name);
            }
        }

        yield return null;
    }

    /// <summary>
    /// 版本1是否比版本2新
    /// </summary>
    /// <param name="versionStr1"></param>
    /// <param name="versionStr2"></param>
    /// <returns></returns>
    private bool CompareVersion(string versionStr1, string versionStr2)
    {
        if (string.IsNullOrEmpty(versionStr1))
            return false;

        if (string.IsNullOrEmpty(versionStr2))
            return true;

        System.Version version1 = new System.Version(versionStr1);
        System.Version version2 = new System.Version(versionStr2);

        return version1 >= version2;
    }

    /// <summary>
    /// 更新新资源
    /// </summary>
    /// <param name="list"></param>
    /// <returns></returns>
    private IEnumerator UpdateNewRes(List<VersionAssetData> list)
    {
        if (list != null && list.Count > 0)
        {
            foreach (VersionAssetData assetData in list)
            {
                yield return StartCoroutine(DownloadAssetBundle(assetData));
                m_UpdatedResNumber++;
                m_UpdatedSize += assetData.size;
                if (m_IsMovingRes) {
                    if (OnMovingResEvent != null) {
                        OnMovingResEvent();
                    }
                }
                else
                {
                    if (OnUpdatingEvent != null)
                    {
                        OnUpdatingEvent();
                    }
                }
            }
        }
        yield return new WaitForSeconds(1.0f);

        if (m_IsMovingRes)
        {
            MoveResFinished();
        }
        else
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// 下载资源包
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private IEnumerator DownloadAssetBundle(VersionAssetData data)
    {
        string loadPath = GetAssetServerPath(data.path,data.md5);
        WWW www = new WWW(ComTool.FormatTickUrl(loadPath));
        yield return www;
        if (www.error != null)
        {
            Debug.LogError("DownloadAssetBundle Error:" + data.name);
            SendErrorCode(404);
        }
        else
        {
            if (CheckDataValid(data, www.bytes))
            {
                WriteAssetOnLocal(data, www.bytes);
            }
            else {
                Debug.LogError("CheckDataValid false:" + data.name);
            }
        }

        www.Dispose();
    }

    /// <summary>
    /// 写入资源到本地
    /// </summary>
    /// <param name="data"></param>
    /// <param name="bytes"></param>
    private void WriteAssetOnLocal(VersionAssetData data, byte[] bytes)
    {
        //删除之前存在的旧md5码资源
        if (m_LocalRVLDic != null)
        {
            if (m_LocalRVLDic.ContainsKey(data.name))
            {
                string oldPath = m_LocalResRootPath + "/" + m_LocalRVLDic[data.name].md5;
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }
        }

        string localPath = GetAssetLocalPath(data.path,data.md5);
        FileInfo t = new FileInfo(localPath);
        if (t.Exists)
        {
            DeleteAsset(localPath);
        }

        try
        {
            if (!t.Exists)
            {
                Directory.CreateDirectory(t.DirectoryName);
            }

            File.WriteAllBytes(localPath, bytes);
            
        }
        catch (System.Exception ex)
        {
            Debug.LogError(ex.ToString());
            SendErrorCode(201);
        }

        UpdateLocalRVL(data);
    }

    /// <summary>
    /// 更新本地资源版本列表
    /// </summary>
    /// <param name="data"></param>
    private void UpdateLocalRVL(VersionAssetData data)
    {
        if (m_LocalRVLDic == null)
        {
            m_LocalRVLDic = new Dictionary<string, VersionAssetData>();
        }

        string name = GetAssetName(data.name);
        if (m_LocalRVLDic.ContainsKey(name))
        {
            m_LocalRVLDic[name] = data;
        }
        else
        {
            m_LocalRVLDic.Add(name, data);
        }

        WriteLocalRVL();
    }

    /// <summary>
    /// 删除资源
    /// </summary>
    /// <param name="path"></param>
    private void DeleteAsset(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError(ex.ToString());
            SendErrorCode(202);
        }
    }

    /// <summary>
    /// 删除目录
    /// </summary>
    /// <param name="dir"></param>
    private void DeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError(ex.ToString());
            SendErrorCode(202);
        }
    }

    /// <summary>
    /// 移动资源完成
    /// </summary>
    private void MoveResFinished()
    {
        //Debug.Log("Move Finished...");
        if (!CompareVersion(m_LocalVersion, m_ServerVersion))
        {
            m_LocalVersion = m_ServerVersion;
        }
        if (MoveResFinishedEvent != null)
        {
            MoveResFinishedEvent();
        }

        if (m_ServerRVLDic != null)
        {
            m_ServerRVLDic.Clear();
            m_ServerRVLDic = null;
        }

        if (m_UpdateResList != null)
        {
            m_UpdateResList.Clear();
            m_UpdateResList = null;
        }
        m_IsMovingRes = false;
    }

    /// <summary>
    /// 结束更新
    /// </summary>
    private void EndUpdate()
    {
        m_LocalVersion = m_ServerVersion;
        WriteLocalRVL();
        if (CheckUpdateEvent != null)
        {
            CheckUpdateEvent(false);
        }

        RemoveUpdater();
    }

    /// <summary>
    /// 发送错误码
    /// </summary>
    /// <param name="error"></param>
    private void SendErrorCode(int error)
    {
        Debug.LogError(error);
        if (UpdateErrorEvent != null)
        {
            UpdateErrorEvent(error);
        }
    }

    /// <summary>
    /// 检测资源完整合理性
    /// </summary>
    /// <param name="data"></param>
    /// <param name="bytes"></param>
    /// <returns></returns>
    private bool CheckDataValid(VersionAssetData data, byte[] bytes)
    {
        string md5 = GetMd5(bytes);
        if (data.md5 != md5)
        {
            SendErrorCode(401);
        }

        return true;
    }

    /// <summary>
    /// 获取Md5码
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    private string GetMd5(byte[] bytes)
    {
        MD5 md5 = MD5.Create();
        byte[] mds = md5.ComputeHash(bytes);
        string md5Str = "";
        for (int i = 0; i < mds.Length; i++)
        {
            md5Str = md5Str + mds[i].ToString("X");
        }

        return md5Str;
    }

    /// <summary>
    /// 获取资源名称（由路径与Id组合，防止重复）
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    private string GetAssetName(string name)
    {
        name.ToLower();
        name.Replace(" ", "");

        return name;
    }

    /// <summary>
    /// 获取服务器资源地址
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    private string GetAssetServerPath(string path,string md5)
    {
        string add="";
        if (path != "") {
            add = "/" + path;
        }
        string loadPath = m_ServerResRootPath + add + "/" + md5;
        if (m_IsMovingRes)
        {
            loadPath = m_StreamResRootPath + add + "/" + md5;
        }

        loadPath.ToLower();
        loadPath.Replace(" ", "");

        return loadPath;
    }

    /// <summary>
    /// 获取本地路径地址
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    private string GetAssetLocalPath(string path,string md5)
    {
        string localPath = m_LocalResRootPath + "/" + path + "/" + md5;
        if (path == "")
        {
            localPath = m_LocalResRootPath + "/" + md5;
        }
        

        localPath.ToLower();
        localPath.Replace(" ", "");

        return localPath;
    }

    /// <summary>
    /// 移除更新器
    /// </summary>
    private void RemoveUpdater()
    {
        if (m_LocalRVLDic != null)
        {
            m_LocalRVLDic.Clear();
            m_LocalRVLDic = null;
        }
        if (m_UpdateResList != null)
        {
            m_UpdateResList.Clear();
            m_UpdateResList = null;
        }
        if (m_ServerRVLDic != null)
        {
            m_ServerRVLDic.Clear();
            m_ServerRVLDic = null;
        }


        UnityEngine.Object.Destroy(this);
        System.GC.Collect();
    }

    /// <summary>
    /// 解析资源版本列表
    /// </summary>
    /// <param name="xml"></param>
    /// <returns></returns>
    private Dictionary<string, VersionAssetData> ParserResVersionList(string xml, ref string version)
    {
        XmlDocument document = new XmlDocument();
        document.LoadXml(xml);

        Dictionary<string, VersionAssetData> resVersionList = new Dictionary<string, VersionAssetData>();

        XmlNode root = document.SelectSingleNode("root");
        XmlElement rootNode = root as XmlElement;
        if (rootNode != null)
        {
            version = rootNode.GetAttribute("version");
        }
        foreach (XmlNode _node in root.ChildNodes)
        {
            XmlElement node = _node as XmlElement;
            if (node == null) { continue; }
            if (node.Name == "asset")
            {
                VersionAssetData loadAsset = new VersionAssetData();
                loadAsset.name = node.GetAttribute("name");
                loadAsset.md5 = node.GetAttribute("md5");
                loadAsset.size = System.Convert.ToInt32(node.GetAttribute("size"));
                loadAsset.path = node.GetAttribute("path");
                string name = GetAssetName(loadAsset.name);
                if (!resVersionList.ContainsKey(name))
                {
                    resVersionList.Add(name, loadAsset);
                }
            }
        }

        return resVersionList;
    }

    /// <summary>
    /// 写入本地版本列表
    /// </summary>
    private void WriteLocalRVL()
    {
        if (m_LocalRVLDic == null || m_LocalRVLDic.Count == 0)
            return;

        XmlDocument document = new XmlDocument();
        document.CreateXmlDeclaration("1.0", "uft-8", "yes");
        XmlElement rootNode = document.CreateElement("root");
        rootNode.SetAttribute("version", m_LocalVersion);
        foreach (VersionAssetData assetData in m_LocalRVLDic.Values)
        {
            XmlElement assetNode = document.CreateElement("asset");
            assetNode.SetAttribute("name", assetData.name);
            assetNode.SetAttribute("md5", assetData.md5);
            assetNode.SetAttribute("size", assetData.size.ToString());
            assetNode.SetAttribute("path", assetData.path);
            rootNode.AppendChild(assetNode);
        }
        document.AppendChild(rootNode);
        //Debug.LogError("WriteLocalRVL:" + m_LocalResRootPath + "/" + RVL);
        try {
            document.Save(m_LocalResRootPath + "/" + RVL);
            //Debug.LogError("file exist:" + File.Exists(m_LocalResRootPath + "/" + RVL));
            
        }
        catch(Exception e){
            Debug.LogError(e.ToString());
        }
        
    }
}
public delegate void UpdateErrorDelegate(int error);
public delegate void UpdateStateDelegate();
public delegate void CheckUpdateDelegate(bool isNeedUpdate);

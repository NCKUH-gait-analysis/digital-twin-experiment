using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using System.Threading;
using Bones_controller;
using static lab_skeleton;
using static Rotation_controller;
using static HumanBodyTransform;

public class unity_humanbones : MonoBehaviour
{
    // lab_skeleton Lab_skeleton = new lab_skeleton();
    Rotation_controller rotation_controller = new Rotation_controller();
    Bone_controller Controller = new Bone_controller();

    lab_skeleton Lab_skeleton1 = new lab_skeleton();
    lab_skeleton Lab_skeleton2 = new lab_skeleton();
    // lab_skeleton Lab_skeleton3 = new lab_skeleton();

    static public GameObject Physical;
    static public GameObject Virtual;
    // static public GameObject Frag;

    HumanBodyTransform HumanBodyTransform1; 
    HumanBodyTransform HumanBodyTransform2;
    // HumanBodyTransform HumanBodyTransform3; 

    // 動作資料的原生取樣率(Hz)。播放改為時間驅動:
    // 人物速度與渲染 fps 脫鉤,掛虛擬相機或場景變重都不會讓人物變慢。
    // 120 的依據:hip 在資料中位移 4.07m(= 4m 步道),476 幀在 120Hz 下
    // = 4.0s、步速 1.03 m/s(正常步行);若為 60Hz 步速僅 0.51 m/s,不合理。
    public float dataFps = 120f;
    float playTime = 0f;
    int count = 0;

    void Start()
    {
        // Set update fps: 10
        // Application.targetFrameRate = 60;

        // 用 Application.dataPath 讓編輯器(<專案>/Assets)與獨立播放器
        // (<exe>_Data,由 BuildTestPlayer 複製 Motion Data 進去)都讀得到
        string dataDir = System.IO.Path.Combine(Application.dataPath, "Motion data");
        Lab_skeleton1.txt_reader(System.IO.Path.Combine(dataDir, "0508_p1_gait_3.txt"));
        Lab_skeleton2.txt_reader(System.IO.Path.Combine(dataDir, "unity_0508_p1_gait_3.txt"));
        // Lab_skeleton3.txt_reader(@"Assets/Motion Data/s_09_act_02_subact_01_ca_03_frag.txt");

        // Frag = GameObject.Find("Frag");
        Physical = GameObject.Find("physical");
        Virtual = GameObject.Find("virtual");

        HumanBodyTransform1 = new HumanBodyTransform(Physical.GetComponent<Animator>());
        HumanBodyTransform2 = new HumanBodyTransform(Virtual.GetComponent<Animator>());
        // HumanBodyTransform3 = new HumanBodyTransform(Frag.GetComponent<Animator>()); 

    }
    
    void Update()
    {
        // 以牆鐘時間換算資料幀索引,而非每渲染幀 +1
        playTime += Time.deltaTime;
        count = (int)(playTime * dataFps);
        // Update the bones for each lab_skeleton instance
        UpdateBones(Lab_skeleton1, HumanBodyTransform1);
        UpdateBones(Lab_skeleton2, HumanBodyTransform2);
        // UpdateBones(Lab_skeleton3, HumanBodyTransform3);
    }

    void UpdateBones(lab_skeleton labSkeleton, HumanBodyTransform humanbodytransform)
    {
        // 注意:2D 陣列的 Length 是「總元素數」(幀數x15),幀數要用 GetLength(0)
        int frameCount = labSkeleton.coordinate_list.GetLength(0);
        if (frameCount == 0)
            return;
        int idx = count % frameCount;   // 循環播放,測試時人物持續行走
        // Apply bone transformations based on the coordinate list
        rotation_controller.Lab_Rotation(idx, labSkeleton, humanbodytransform);
        if (idx > 0) {
            float newZ = labSkeleton.coordinate_list[idx, 8][2] + 2;
            Vector3 newPosition = new Vector3(humanbodytransform.Hip.position.x, humanbodytransform.Hip.position.y, newZ);
            // Set the new position
            humanbodytransform.Hip.position = newPosition;
        }
    }
    
}


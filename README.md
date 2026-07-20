# AoQAudioMod

A sound overhaul / sound effect replacement mod for the PCVR version of Attack on Quest built with BepInEx 5.

# Features

- More faithful / accurate sounds for ODM, titans, etc.

- A sounds folder by which sounds can be read to be replaced in game

# Installation

- Extract the AOQAudioMod.zip file into the BepInEx plugins folder
  - The mod will have a dedicated folder with a separate Sounds folder to read sounds from

# Requirements

- BepInEx 5.4.x for AoQ

# Implementing custom sounds

 Sound files must be the following:

  - .wav

  - 44100hz sampling rate

  - 16 bit encoding
  
  - stereo
    
- anything different is not guaranteed to work!

- MUST have the same name as the original file! However, you can assign multiple sounds to one action or event in game by adding \_1, \_2, \_3, etc. to the end of the file name. Doing so will have one of the sounds played at random each time the event occurs

- original sound file names for reference can be viewed by loading the AttackOnQuest\_Data folder in a program such as AssetStudio





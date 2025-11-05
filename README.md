# PSOCUnityEditorTools

This repository contains tools and scripts for the PSOC System for Unity Editor, designed to enhance the development experience by automating scriptable object creation and management. This can be a valuable resource for developers looking to streamline their workflow, or release this as a service for eventual modding support.

# What is PSOC?

PSOC stands for "Phils Scriptable Object Composer" and it is a set of tools that allows you to automate tasks using LLMs (Large Language Models) that would usually be done by experienced hands in Unity. Specifically, this tool allows you to automate workflows that create game content by creating and composing scriptable objects in Unity. When creating this content, the LLMs can use your game design documents and guidelines to generate content that fits your game.

This is not a coding helper, but a content creation helper. It is designed to help you create game content like creatures, items, and other game objects by using the power of LLMs to generate and compose scriptable objects in Unity.

# Setup 

PSOC requires access to a local hosted or online backend that handles the LLM requests, this package is only for you to link existing resources to the psoc system. 

## Install and run the docker container

Backend not released yet.

## Unity Package Installation

Install via git url or download the repository and import it as a custom package in Unity.

# Usage

The content creation workflows are made of Agents, Tools, Databuckets and workflows.
All these ScriptableObjects can be created using the right click menu Create > PSOC Unity Editor Tools > ...

## Databuckets
Databuckets are ScriptableObjects that hold data that can be used by the agents and tools during the workflows. You can create databuckets point to a folder and recursively makes all text files in that folder available to the agents and tools.

## Tools 

Tools are ScriptableObjects that perform specific tasks and are used by the agents during the workflows. You can create tools that perform tasks like generating text, summarizing text, extracSting keywords, selecting options, and more.

TODO describe subtypes of tools.

Tools are almost always supposed to work with databuckets to get the data they need to perform their tasks.

Typical examples and their databucket usage:
- A text generation tool that uses a databucket containing game design documents to generate content that fits the game's lore and style.
- An option selection tool that uses a databucket containing a list of possible options to choose from based on specific criteria.
- A text summarization tool that uses a databucket containing lengthy text documents to generate concise summaries.

## Agents
Agents are ScriptableObjects that use tools to perform specific workflows. You can create agents and instruct them to use specific tools to perform subsets of the content creation, like writing a description, generating stats, or choosing a set of options.

## Workflows
Workflows are a chain of agents that work together to create a final output. You can create workflows that use multiple agents to create complex content by breaking down the content creation into smaller, manageable tasks.

Worfkflows currently need to be manually implemented in the container that handles the LLM requests, but future versions will allow you to create and manage workflows directly in Unity.

The workflows always start with a query refiner agent that takes the user input and refines it into a more specific query that can be used by the subsequent agents.

The last 2 nodes must be a wrapup node that collects all the output from the previous agents and a finalizer node that takes the wrapup output and creates the final scriptable object in Unity.



TODOs:
- Installation instructions.
- Usage instructions.
- Limitations and known issues.


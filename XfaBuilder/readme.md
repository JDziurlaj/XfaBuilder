#Overview#

XFA Builder allows users to create and manipulate dynamic PDF documents.

#Uses#
XML Forms Architecture (XFA) is a standard that extends the capabilities of various presentation engines (e.g. PDF views, web browsers) to support a rich, declarative environment for capturing business data. This technology is most often associated with the Adobe LiveCycle and Acrobat product lines, for producing and consuming such documents in the PDF format.

Dynamic XFA documents that are stored as PDF, unlike most other PDF documents, express their presentation details purely in XML. Viewers that do not understand this XML representation fall back on a "shell PDF", usually informing the user to upgrade their PDF viewer. Due to the pervasiveness of mobile devices, and browsers who often do not support XFA forms, more and more users are encountering this "error". 

XfaBuilder allows a user to generate their own shell PDF, which allows fine grain control over the messages displayed and the XDP packets included.    
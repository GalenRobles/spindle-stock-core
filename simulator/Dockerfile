FROM python:3.12-slim

WORKDIR /app

COPY simulator/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY simulator/ ./simulator/
COPY Data/ ./data/

EXPOSE 8090
CMD ["python", "simulator/simulator.py"]
